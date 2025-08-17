using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Configuration
{
    public sealed class ServiceBusOptions
    {
        public string QueueName { get; set; } = "mft-queue";
        public string NamespaceFqdn { get; set; } = "";
        public int MaxConcurrentCalls { get; set; } = 1;
    }
}
