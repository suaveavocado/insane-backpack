using System;
using Microsoft.Extensions.Options;

namespace SuaveAvocado.Sensor.Mock.Secrets
{
    public class SecretRevealer : ISecretRevealer
    {
        private readonly DeviceConfiguration _secrets;
        // I’ve injected <em>secrets</em> into the constructor as setup in Program.cs
        public SecretRevealer(IOptions<DeviceConfiguration> secrets)
        {
            // We want to know if secrets is null so we throw an exception if it is
            _secrets = secrets.Value ?? throw new ArgumentNullException(nameof(secrets));
        }

        public void Reveal()
        {
            //I can now use my mapped secrets below.
            Console.WriteLine($"ConnectionString: {_secrets.ConnectionString}");
        }
    }
}