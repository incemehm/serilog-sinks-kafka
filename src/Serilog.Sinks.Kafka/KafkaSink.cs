using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Confluent.Kafka;
using Serilog.Context;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.PeriodicBatching;

namespace Serilog.Sinks.Kafka
{
    public class KafkaSink : IBatchedLogEventSink
    {
        private const int FlushTimeoutSecs = 10;

        private readonly TopicPartition _globalTopicPartition;
        private readonly ITextFormatter _formatter;
        private readonly Func<LogEvent, string> _topicDecider;
        Action<IProducer<string, byte[]>, Error> _errorHandler;
        ProducerConfig _producerConfig;
        string messageKey;
        const string SKIP_KEY = "skip-kafka";

        public KafkaSink(
            ProducerConfig producerConfig,
            string topic = null,
            Func<LogEvent, string> topicDecider = null,
             ITextFormatter formatter = null, string messageKey = null, Action<IProducer<string, byte[]>, Error> errorHandler = null)
        {
            Console.WriteLine($"[Kafka] new topic={topic}");

            _formatter = formatter ?? new Formatting.Json.JsonFormatter(renderMessage: true);

            if (topic != null)
                _globalTopicPartition = new TopicPartition(topic, Partition.Any);

            if (topicDecider != null)
                _topicDecider = topicDecider;

            if (_errorHandler != null)
                _errorHandler = errorHandler;
            else
            {
                _errorHandler = (pro, msg) =>
                {
                    Log.ForContext(SKIP_KEY, string.Empty).Error($"[Kafka] Error {pro.Name} {msg.Code} {msg.Reason}");
                };
            }

            this.messageKey = messageKey;
            this._producerConfig = producerConfig;
            ConfigureKafkaConnection();

            Console.WriteLine($"[Kafka] Producer OK");
        }

        public Task OnEmptyBatchAsync() => Task.CompletedTask;

        public Task EmitBatchAsync(IEnumerable<LogEvent> batch)
        {
            try
            {
                using var producer = ConfigureKafkaConnection();

                foreach (var logEvent in batch)
                {
                    if (logEvent.Properties.ContainsKey(SKIP_KEY))
                        continue;

                    Message<string, byte[]> message;

                    var topicPartition = _topicDecider == null
                        ? _globalTopicPartition
                        : new TopicPartition(_topicDecider(logEvent), Partition.Any);

                    using (var render = new StringWriter(CultureInfo.InvariantCulture))
                    {
                        _formatter.Format(logEvent, render);

                        string key = null;
                        if (!string.IsNullOrEmpty(messageKey) && logEvent.Properties.TryGetValue(messageKey, out LogEventPropertyValue value))
                            key = value.ToString();

                        message = new Message<string, byte[]>
                        {
                            Key = key,
                            Value = Encoding.UTF8.GetBytes(render.ToString())
                        };
                    }

                    producer.Produce(topicPartition, message, report => {
                        Log.ForContext(SKIP_KEY, string.Empty).Debug($"[Kafka]：{report.Error.Reason} {report.Status}");
                    });
                }

                var count = producer.Flush(TimeSpan.FromSeconds(FlushTimeoutSecs));
                Log.ForContext(SKIP_KEY, string.Empty).Debug($"[Kafka]: Flush {count}");
            }
            catch (Exception ex)
            {
                Log.ForContext(SKIP_KEY, string.Empty).Error(ex, "[Kafka][EmitBatchAsync Error]");
                Log.ForContext(SKIP_KEY, string.Empty).Information($"[Kafka][batchInfo] {batch.First().RenderMessage()} ~ {batch.Last().RenderMessage()}");
            }

            return Task.CompletedTask;
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