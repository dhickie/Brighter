using System;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.Observability;
using Paramore.Brighter.Outbox.Hosting;

namespace Paramore.Brighter.IoC.Tests
{
    public class OutboxIoCTests
    {
        [Fact]
        public void When_configuring_an_outbox_archiver()
        {
            var services = new ServiceCollection();

            var internalBus = new InternalBus();
            var producers = new[]
            {
                new Publication
                {
                    Topic = new RoutingKey("Topic")
                }
            };
            var producerRegistry = new InMemoryProducerRegistryFactory(internalBus, producers, InstrumentationOptions.All).Create();
            var outbox = new InMemoryOutbox(TimeProvider.System);
            var brighterBuilder = services.AddBrighter()
                .AddProducers(config =>
                {
                    config.ProducerRegistry = producerRegistry;
                    config.Outbox = outbox;
                    config.TransactionProvider = typeof(CommittableTransactionProvider);
                    config.ArchiveProvider = new NullOutboxArchiveProvider();
                })
                .UseOutboxArchiver<CommittableTransactionProvider>(new NullOutboxArchiveProvider());

            var serviceProvider = services.BuildServiceProvider();
            var timedArchiver = serviceProvider.GetService<TimedOutboxArchiver<Message, CommittableTransactionProvider>>();

            Assert.NotNull(timedArchiver);
        }
    }
}
