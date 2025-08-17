using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Configuration
{
    public sealed class AuthOptions
    {
        public string Mode { get; set; } = "managedIdentity";
        public string StorageAccountUrl { get; set; } = "";
    }
}
