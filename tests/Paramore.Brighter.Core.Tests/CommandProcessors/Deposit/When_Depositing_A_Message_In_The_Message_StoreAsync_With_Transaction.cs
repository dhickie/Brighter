﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Paramore.Brighter.Core.Tests.CommandProcessors.TestDoubles;
using Paramore.Brighter.Observability;
using Polly;
using Polly.Registry;
using Xunit;

namespace Paramore.Brighter.Core.Tests.CommandProcessors.Deposit
{
    [Collection("CommandProcessor")]
    public class CommandProcessorDepositPostWithTransactionTestsAsync : IDisposable
    {
        private readonly RoutingKey _routingKey = new("MyCommand");

        private readonly CommandProcessor _commandProcessor;
        private readonly MyCommand _myCommand = new MyCommand();
        private readonly Message _message;
        private readonly SpyOutbox _spyOutbox;
        private readonly SpyTransactionProvider _transactionProvider = new();
        private readonly InternalBus _internalBus = new();

        public CommandProcessorDepositPostWithTransactionTestsAsync()
        {
            _myCommand.Value = "Hello World";

            var timeProvider = new FakeTimeProvider();
            InMemoryProducer producer = new(_internalBus, timeProvider)
            {
                Publication = {Topic = _routingKey, RequestType = typeof(MyCommand)}
            };

            _message = new Message(
                new MessageHeader(_myCommand.Id, _routingKey, MessageType.MT_COMMAND),
                new MessageBody(JsonSerializer.Serialize(_myCommand, JsonSerialisationOptions.Options))
                );

            var messageMapperRegistry = new MessageMapperRegistry(
                null,
                new SimpleMessageMapperFactoryAsync((_) => new MyCommandMessageMapperAsync())
            );
            messageMapperRegistry.RegisterAsync<MyCommand, MyCommandMessageMapperAsync>();

            var retryPolicy = Policy
                .Handle<Exception>()
                .RetryAsync();

            var circuitBreakerPolicy = Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(1, TimeSpan.FromMilliseconds(1));

            var producerRegistry =
                new ProducerRegistry(new Dictionary<RoutingKey, IAmAMessageProducer>
                {
                    { _routingKey, producer },
                });

            var policyRegistry = new PolicyRegistry
            {
                { CommandProcessor.RETRYPOLICYASYNC, retryPolicy },
                { CommandProcessor.CIRCUITBREAKERASYNC, circuitBreakerPolicy }
            };

            var tracer = new BrighterTracer();
            _spyOutbox = new SpyOutbox() {Tracer = tracer};
            
            IAmAnOutboxProducerMediator bus = new OutboxProducerMediator<Message, SpyTransaction>(
                producerRegistry, 
                policyRegistry,
                messageMapperRegistry,
                new EmptyMessageTransformerFactory(),
                new EmptyMessageTransformerFactoryAsync(),
                tracer,
                _spyOutbox
            );
        
            CommandProcessor.ClearServiceBus();
            _commandProcessor = new CommandProcessor(
                new InMemoryRequestContextFactory(), 
                policyRegistry,
                bus,
                _transactionProvider
            );
        }


        [Fact]
        public async Task When_depositing_a_message_in_the_outbox_with_a_transaction_async()
        {
            //act
            var postedMessageId = await _commandProcessor.DepositPostAsync(_myCommand);
            var context = new RequestContext();

            //assert

            //message should not be in the outbox
            _spyOutbox.Messages.Any(m => m.Message.Id == postedMessageId).Should().BeFalse();

            //message should be in the current transaction
            var transaction = _transactionProvider.GetTransaction();
            var message = transaction.Get(postedMessageId);
            message.Should().NotBeNull();

            //message should not be posted
            _internalBus.Stream(new RoutingKey(_routingKey)).Any().Should().BeFalse();

            //message should correspond to the command
            message.Id.Should().Be(_message.Id);
            message.Body.Value.Should().Be(_message.Body.Value);
            message.Header.Topic.Should().Be(_message.Header.Topic);
            message.Header.MessageType.Should().Be(_message.Header.MessageType);
        }
        
        public void Dispose()
        {
            CommandProcessor.ClearServiceBus();
        }
    }
}
