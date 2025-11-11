using Confluent.Kafka;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.PeriodicBatching;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Serilog.Sinks.Kafka
{
    public class KafkaSink : IBatchedLogEventSink
    {
        private const int FlushTimeoutSecs = 10;

        private readonly TopicPartition _globalTopicPartition;
        private readonly ITextFormatter _formatter;
        private readonly Action<IProducer<string, byte[]>, Error> _errorHandler;
        private readonly ProducerConfig _producerConfig;
        private readonly IProducer<string, byte[]> _producer;
        private readonly string _messageKey;

        private ActionBlock<(TopicPartition, Message<string, byte[]>)> _actionBlock;

        const string SKIP_KEY = "skip-kafka";
        const int IN_FLIGHT_REQUESTS = 16384;

        public KafkaSink(
            ProducerConfig producerConfig,
            string topic = null,
             ITextFormatter formatter = null, string messageKey = null, Action<IProducer<string, byte[]>, Error> errorHandler = null)
        {
            Console.WriteLine($"[Kafka] new topic={topic}");

            _formatter = formatter ?? new Formatting.Json.JsonFormatter(renderMessage: true);

            if (topic != null)
                _globalTopicPartition = new TopicPartition(topic, Partition.Any);

            if (_errorHandler != null)
                _errorHandler = errorHandler;
            else
            {
                _errorHandler = (pro, msg) =>
                {
                    Log.ForContext(SKIP_KEY, string.Empty).Error($"[Kafka] Error {pro.Name} {msg.Code} {msg.Reason}");
                };
            }

            _actionBlock = new ActionBlock<(TopicPartition, Message<string, byte[]>)>(
                async msg =>
                {
                    try
                    {
                        await _producer.ProduceAsync(msg.Item1, msg.Item2);
                    }
                    catch (Exception ex)
                    {
                        Log.ForContext(SKIP_KEY, string.Empty).Error(ex, "[Kafka][ActionBlock Error]");
                        Log.ForContext(SKIP_KEY, string.Empty).Information($"[Kafka][batchInfo] {msg.Item2.Value}");
                    }
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = IN_FLIGHT_REQUESTS,
                    BoundedCapacity = IN_FLIGHT_REQUESTS
                });

            _messageKey = messageKey;
            _producerConfig = producerConfig;
            _producer = ConfigureKafkaConnection();

            Console.WriteLine($"[Kafka] Producer OK");
        }

        public Task OnEmptyBatchAsync() => Task.CompletedTask;

        public async Task EmitBatchAsync(IEnumerable<LogEvent> batch)
        {
            try
            {
                foreach (var logEvent in batch)
                {
                    if (logEvent.Properties.ContainsKey(SKIP_KEY))
                        continue;

                    Message<string, byte[]> message;

                    using (var render = new StringWriter(CultureInfo.InvariantCulture))
                    {
                        _formatter.Format(logEvent, render);

                        string key = null;
                        if (!string.IsNullOrEmpty(_messageKey) && logEvent.Properties.TryGetValue(_messageKey, out LogEventPropertyValue value))
                            key = value.ToString();

                        var log = Encoding.UTF8.GetBytes(render.ToString());
                        message = new Message<string, byte[]>
                        {
                            Key = key,
                            Value = log
                        };
                    }

                    await _actionBlock.SendAsync((_globalTopicPartition, message));
                }

                _producer.Flush(TimeSpan.FromSeconds(FlushTimeoutSecs));
            }
            catch (Exception ex)
            {
                Log.ForContext(SKIP_KEY, string.Empty).Error(ex, "[Kafka][EmitBatchAsync Error]");
                Log.ForContext(SKIP_KEY, string.Empty).Information($"[Kafka][batchInfo] {batch.First().RenderMessage()} ~ {batch.Last().RenderMessage()}");
            }
        }

        private IProducer<string, byte[]> ConfigureKafkaConnection()
        {
            return new ProducerBuilder<string, byte[]>(_producerConfig)
                    .SetErrorHandler(_errorHandler)
                    .SetLogHandler((pro, msg) =>
                    {
                        if (msg.Level <= SyslogLevel.Error)
                            Log.ForContext(SKIP_KEY, string.Empty).Error($"[Kafka] {msg.Level} {msg.Message}");
                        else
                            Log.ForContext(SKIP_KEY, string.Empty).Information($"[Kafka] {msg.Level} {msg.Message}");
                    })
                    .Build();
        }
    }
}