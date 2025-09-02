using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Configuration
{
    using Microsoft.Extensions.Options;
    public class ServiceBusOptionsValidator : IValidateOptions<Worker.Configuration.ServiceBusOptions>
    {
        public ValidateOptionsResult Validate(string? name, Worker.Configuration.ServiceBusOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.NamespaceFqdn)) return ValidateOptionsResult.Fail("ServiceBus:NamespaceFqdn is required");
            if (string.IsNullOrWhiteSpace(options.QueueName)) return ValidateOptionsResult.Fail("ServiceBus:QueueName is required");
            if (options.MaxRenewHours <= 0) return ValidateOptionsResult.Fail("ServiceBus:MaxRenewHours must be > 0");
            return ValidateOptionsResult.Success;
        }
    }
    public class ProcessingOptionsValidator : IValidateOptions<Worker.Configuration.ProcessingOptions>
    {
        public ValidateOptionsResult Validate(string? name, Worker.Configuration.ProcessingOptions opts)
        {
            if (opts.MaxDegreeOfParallelism <= 0) return ValidateOptionsResult.Fail("Processing:MaxDegreeOfParallelism must be > 0");
            return ValidateOptionsResult.Success;
        }
    }
    public class StorageOptionsValidator : IValidateOptions<Worker.Configuration.StorageOptions>
    {
        public ValidateOptionsResult Validate(string? name, Worker.Configuration.StorageOptions opts)
        {
            if (string.IsNullOrWhiteSpace(opts.Connection)) return ValidateOptionsResult.Fail("Storage:Connection is required");
            if (string.IsNullOrWhiteSpace(opts.StageContainer)) return ValidateOptionsResult.Fail("Storage:StageContainer is required");
            if (string.IsNullOrWhiteSpace(opts.ProdContainer)) return ValidateOptionsResult.Fail("Storage:ProdContainer is required");
            return ValidateOptionsResult.Success;
        }
    }
    public class DatabaseOptionsValidator : IValidateOptions<Worker.Configuration.DatabaseOptions>
    {
        public ValidateOptionsResult Validate(string? name, Worker.Configuration.DatabaseOptions opts)
        {
            if (string.IsNullOrWhiteSpace(opts.Postgres)) return ValidateOptionsResult.Fail("ConnectionStrings:Postgres is required");
            return ValidateOptionsResult.Success;
        }
    }
}
