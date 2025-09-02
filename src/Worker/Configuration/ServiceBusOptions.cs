using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Configuration
{
    public class ServiceBusOptions
    {
        public string NamespaceFqdn { get; set; } = "";
        public string QueueName { get; set; } = "emea";
        public int MaxRenewHours { get; set; } = 8;
        public int MaxRetries { get; set; } = 2;
        public string ConnectionString { get; set; } = "";
        public bool IsManagedConnection { get; set; }=false;
    }
}
