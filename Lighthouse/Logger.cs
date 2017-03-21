using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;

namespace Lighthouse
{
    internal class Logger : ReceiveActor, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        public Logger()
        {
            Receive<Error>(m => LogEvent("ERROR", $"{m.Message}: {m.Cause}"));
            Receive<Warning>(m => LogEvent("WARN", m.Message));
            Receive<Info>(m => LogEvent("INFO", m.Message));
            Receive<Debug>(m => LogEvent("DEBUG", m.Message));
            Receive<InitializeLogger>(m =>
            {
                Sender.Tell(new LoggerInitialized());
            });
        }

        private static void LogEvent(string level, object message)
        {
            ServiceEventSource.Current.ServiceMessage(Lighthouse.LastContext, $"{level} - {message}");
        }
    }
}
