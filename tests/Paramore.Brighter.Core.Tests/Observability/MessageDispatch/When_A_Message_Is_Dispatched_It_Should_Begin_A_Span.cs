﻿#region Licence
/* The MIT License (MIT)
Copyright © 2014 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE. */

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Paramore.Brighter.Core.Tests.CommandProcessors.TestDoubles;
using Paramore.Brighter.Observability;
using Paramore.Brighter.ServiceActivator;
using Polly.Registry;
using Xunit;

namespace Paramore.Brighter.Core.Tests.Observability.MessageDispatch
{
    public class MessagePumpDispatchObservabilityTests
    {
        private const string Topic = "MyTopic";
        private const string ChannelName = "myChannel";
        private readonly RoutingKey _routingKey = new(Topic);
        private readonly InternalBus _bus = new();
        private readonly FakeTimeProvider _timeProvider = new();
        private readonly IAmAMessagePump _messagePump;
        private readonly MyEvent _myEvent = new();
        private readonly IDictionary<string, string> _receivedMessages = new Dictionary<string, string>();
        private readonly List<Activity> _exportedActivities;
        private readonly TracerProvider _traceProvider;
        private readonly Message _message;

        public MessagePumpDispatchObservabilityTests()
        {
            var builder = Sdk.CreateTracerProviderBuilder();
            _exportedActivities = new List<Activity>();

            _traceProvider = builder
                .AddSource("Paramore.Brighter.Tests", "Paramore.Brighter")
                .ConfigureResource(r => r.AddService("in-memory-tracer"))
                .AddInMemoryExporter(_exportedActivities)
                .Build();
        
            Brighter.CommandProcessor.ClearServiceBus();
            
            var subscriberRegistry = new SubscriberRegistry();
            subscriberRegistry.Register<MyEvent, MyEventHandler>();

            var handlerFactory = new SimpleHandlerFactorySync(_ => new MyEventHandler(_receivedMessages));
            
            var timeProvider  = new FakeTimeProvider();
            var tracer = new BrighterTracer(timeProvider);
            var instrumentationOptions = InstrumentationOptions.All;
            
            var commandProcessor = new Brighter.CommandProcessor(
                subscriberRegistry,
                handlerFactory, 
                new InMemoryRequestContextFactory(), 
                new PolicyRegistry(),
                tracer: tracer,
                instrumentationOptions: instrumentationOptions);

            var provider = new CommandProcessorProvider(commandProcessor);
            
            PipelineBuilder<MyEvent>.ClearPipelineCache();

            var channel = new Channel(new(ChannelName),_routingKey, new InMemoryMessageConsumer(_routingKey, _bus, _timeProvider, 1000));
            var messageMapperRegistry = new MessageMapperRegistry(
                new SimpleMessageMapperFactory(
                    _ => new MyEventMessageMapper()),
                null); 
            messageMapperRegistry.Register<MyEvent, MyEventMessageMapper>();
            
            _messagePump = new MessagePumpBlocking<MyEvent>(provider, messageMapperRegistry, null, 
                new InMemoryRequestContextFactory(), tracer, instrumentationOptions)
            {
                Channel = channel, TimeoutInMilliseconds = 5000
            };
            
            var externalActivity = new ActivitySource("Paramore.Brighter.Tests").StartActivity("MessagePumpSpanTests");

            var header = new MessageHeader(_myEvent.Id, Topic, MessageType.MT_EVENT)
            {
                TraceParent = externalActivity?.Id, TraceState = externalActivity?.TraceStateString
            };
            
            externalActivity?.Stop();

            _message = new Message(
                header, 
                new MessageBody(JsonSerializer.Serialize(_myEvent, JsonSerialisationOptions.Options))
            );
            
            channel.Enqueue(_message);
            var quitMessage = MessageFactory.CreateQuitMessage(new RoutingKey(Topic));
            channel.Enqueue(quitMessage);
        }

        [Fact]
        public void When_a_message_is_dispatched_it_should_begin_a_span()
        {
            _messagePump.Run();

            _traceProvider.ForceFlush();
            
            _exportedActivities.Count.Should().Be(6);
            _exportedActivities.Any(a => a.Source.Name == "Paramore.Brighter").Should().BeTrue();
            
            //there should be a span for each message received by a pump
            var createActivity = _exportedActivities.FirstOrDefault(a => 
                a.DisplayName == $"{_message.Header.Topic} {MessagePumpSpanOperation.Receive.ToSpanName()}"
                && a.TagObjects.Any(to => to is { Value: not null, Key: BrighterSemanticConventions.MessageType } && Enum.Parse<MessageType>(to.Value.ToString() ?? string.Empty) == MessageType.MT_EVENT)
                );
            createActivity.Should().NotBeNull();
            createActivity!.ParentId.Should().Be(_message.Header.TraceParent);
            createActivity.Tags.Any(t => t is { Key: BrighterSemanticConventions.MessagingOperationType, Value: "receive" }).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.MessagingDestination && t.Value == _message.Header.Topic).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.MessagingDestinationPartitionId && t.Value == _message.Header.PartitionKey).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.MessageId && t.Value == _message.Id).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.MessageType && t.Value == _message.Header.MessageType.ToString()).Should().BeTrue();
            createActivity.TagObjects.Any(t => t.Value != null && t.Key == BrighterSemanticConventions.MessageBodySize && Convert.ToInt32(t.Value) == _message.Body.Bytes.Length).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.MessageBody && t.Value == _message.Body.Value).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.MessageHeaders && t.Value == JsonSerializer.Serialize(_message.Header)).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.ConversationId && t.Value == _message.Header.CorrelationId).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.MessagingSystem && t.Value == MessagingSystem.InternalBus.ToMessagingSystemName()).Should().BeTrue();
            createActivity.TagObjects.Any(t => t.Value != null && t.Key == BrighterSemanticConventions.CeSource && ((Uri)(t.Value)) == _message.Header.Source).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.CeSubject && t.Value == _message.Header.Subject).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.CeVersion && t.Value == "1.0").Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.CeType && t.Value == _message.Header.Type).Should().BeTrue();
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.CeMessageId && t.Value == _message.Id).Should().BeTrue();
            createActivity.TagObjects.Any(t => t.Key == BrighterSemanticConventions.HandledCount && Convert.ToInt32(t.Value) == _message.Header.HandledCount).Should().BeTrue(); 
            createActivity.Tags.Any(t => t.Key == BrighterSemanticConventions.ReplyTo && t.Value == _message.Header.ReplyTo).Should().BeTrue();

        }
    }
}