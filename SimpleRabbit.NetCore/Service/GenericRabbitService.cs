using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace SimpleRabbit.NetCore
{
    public interface IGenericRabbitService : IBasicRabbitService
    {
        void CreateExchange(string name);
        void CreateQueue(string name);
        void CreateQueue(string name, string exchange);
    }

    public class GenericRabbitService : BasicRabbitService, IGenericRabbitService
    {
        /// <summary>
        /// A Timer to check for an idle connection, to ensure a connection is not held open indefinitely.
        /// </summary>
        /// <remarks> 
        /// Threading in the Connection prevent Console Applications from stopping if the connection
        /// is not closed (i.e inside a using clause or not calling close).
        /// </remarks>
        private readonly Timer _watchdogTimer;

        private readonly ILogger<PublishService> _logger;
        protected long lastWatchDogTicks;


        public int InactivityPeriod { get; set; }

        public GenericRabbitService(ILogger<PublishService> logger, RabbitConfiguration options) : base(options)
        {
            InactivityPeriod = 30;

            _watchdogTimer = new Timer
            {
                AutoReset = true,
                Interval = InactivityPeriod * 1000, // in seconds
                Enabled = false
            };

            _watchdogTimer.Elapsed += (sender, args) => { WatchdogExecution(); };

            lastWatchDogTicks = DateTime.UtcNow.Ticks;
            _logger = logger;
        }


        private void WatchdogExecution()
        {
            var acquired = false;
            try
            {
                _logger.LogTrace("Checking Watchdog timer");
                System.Threading.Monitor.TryEnter(this, ref acquired);
                if (!acquired)
                {
                    return;
                }

                OnWatchdogExecution();
            }
            finally
            {
                if (acquired)
                {
                    System.Threading.Monitor.Exit(this);
                }
            }
        }

        protected void OnWatchdogExecution()
        {
            if (lastWatchDogTicks >= DateTime.UtcNow.AddSeconds(-InactivityPeriod).Ticks)
            {
                return;
            }

            _logger.LogInformation($"Idle Rabbit Publishing Connection Detected, Clearing connection");
            lastWatchDogTicks = DateTime.UtcNow.Ticks;
            Close();
            _watchdogTimer.Stop();
        }

        protected override void Cleanup()
        {
            base.Cleanup();
            _watchdogTimer?.Dispose();
        }

        public void CreateExchange(string name)
        {
            Channel.ExchangeDeclare(name, ExchangeType.Direct);
        }

        public void CreateQueue(string exchange, string name)
        {
            CreateQueue(name);
            Channel.QueueBind(name, exchange, name);
        }

        public void CreateQueue(string name)
        {
            Channel.QueueDeclare(name, false, false, false, null);
        }
    }
}