using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Configuration
{
    public class StorageOptions
    {
        public string Connection { get; set; } = "";
        public string StageContainer { get; set; } = "";
        public string ProdContainer { get; set; } = "";
    }
}
