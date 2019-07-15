using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SuaveAvocado.Contacts;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuaveAvocado.Sensor.Mock
{
    class Program
    {
        private static DeviceClient _device;
        private static TwinCollection _reportedProperties;

        public static IConfigurationRoot Configuration { get; set; }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Initializing Band Agent...");

            ConfigureEnviroment();

            var deviceConnectionString = Configuration["mock-01:connectionString"];
            _device = DeviceClient.CreateFromConnectionString(deviceConnectionString);

            await _device.OpenAsync();
            _ = ReceiveEvents(_device);

            await _device.SetMethodDefaultHandlerAsync(DefaultMessage, null);
            await _device.SetMethodHandlerAsync("showMessage", ShowMessage, null);

            Console.WriteLine("Device is Connected");

            await UpdateTwin(_device);
            await _device.SetDesiredPropertyUpdateCallbackAsync(UpdateProperties, null);

            Console.WriteLine("Press a key to preform an action:");
            Console.WriteLine("q: quits");
            Console.WriteLine("h: send happy feedback");
            Console.WriteLine("u: send unhappy feedback");
            Console.WriteLine("e: request emergency help");

            var random = new Random();
            var quitRequested = false;
            while (!quitRequested)
            {
                Console.Write("Action? ");
                var input = Console.ReadKey().KeyChar;
                Console.WriteLine();

                var status = StatusType.NotSpecified;
                var latitude = random.Next(0, 100);
                var longitude = random.Next(0, 100);

                switch (Char.ToLower(input))
                {
                    case 'q':
                        quitRequested = true;
                        break;
                    case 'h':
                        status = StatusType.Happy;
                        break;
                    case 'u':
                        status = StatusType.Happy;
                        break;
                    case 'e':
                        status = StatusType.Emergency;
                        break;
                }

                var telemetry = new Telemetry
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    Status = status
                };

                await SendMessagesToCloudAsync(_device, telemetry);
            }

        }

        private static void ConfigureEnviroment()
        {
            var devEnvironmentVariable = Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT");
            var isDevelopment = string.IsNullOrEmpty(devEnvironmentVariable) || devEnvironmentVariable.ToLower() == "development";
            //Determines the working environment as IHostingEnvironment is unavailable in a console app

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();

            if (isDevelopment) //only add secrets in development
            {
                builder.AddUserSecrets<DeviceConfiguration>();
            }

            Configuration = builder.Build();

            IServiceCollection services = new ServiceCollection();

            services.Configure<DeviceConfiguration>(Configuration.GetSection(nameof(DeviceConfiguration)))
               .AddOptions()
               .AddLogging()
               .AddSingleton<ISecretRevealer, SecretRevealer>()
               .BuildServiceProvider();

            var serviceProvider = services.BuildServiceProvider();

            var revealer = serviceProvider.GetService<ISecretRevealer>();

            revealer.Reveal();

            Console.ReadKey();
        }

        private static async Task ReceiveEvents(DeviceClient device)
        {
            while (true)
            {
                var message = await device.ReceiveAsync();

                if (message == null)
                {
                    continue;
                }

                var messageBody = message.GetBytes();
                var payload = Encoding.ASCII.GetString(messageBody);

                Console.WriteLine($"Received message from cloud: '{payload}'");

                await device.CompleteAsync(message);
            }
        }

        static async Task UpdateTwin(DeviceClient device)
        {
            _reportedProperties = new TwinCollection();
            _reportedProperties["firmwareVersion"] = "1.0";
            _reportedProperties["firmwareUpdateStatus"] = "n/a";

            await device.UpdateReportedPropertiesAsync(_reportedProperties);
        }

        static async Task SendMessagesToCloudAsync(DeviceClient device, Telemetry telemetry)
        {
            var data = JsonConvert.SerializeObject(telemetry);

            var message = new Message(Encoding.ASCII.GetBytes(data));

            await device.SendEventAsync(message);

            Console.WriteLine("Message send to the cloud!");
        }

        private static Task<MethodResponse> DefaultMessage(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("***UNREGISTERED DIRECT METHOD CALLED***");
            Console.WriteLine($"Method: {methodRequest.Name}");
            Console.WriteLine($"Payload: {methodRequest.DataAsJson}");

            var responsePayload = Encoding.ASCII.GetBytes("{\"repsonse\": \"The method is not implemented\" }");

            return Task.FromResult(new MethodResponse(responsePayload, 404));
        }

        private static Task<MethodResponse> ShowMessage(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("***DIRECT MESSAGE RECEIVED***");
            Console.WriteLine(methodRequest.DataAsJson);

            var responsePayload = Encoding.ASCII.GetBytes("{\"response\": \"Message shown!\"}");

            return Task.FromResult(new MethodResponse(responsePayload, 200));
        }

        private static Task UpdateProperties(TwinCollection desiredProperties, object userContext)
        {
            var currentFirmwareVersion = (string)_reportedProperties["firmwareVersion"];
            var desiredFirmwareVersion = (string)desiredProperties["firmwareVersion"];

            if (currentFirmwareVersion != desiredFirmwareVersion)
            {
                Console.Write($"Firemware update requested. Current version: '{currentFirmwareVersion}' -" +
                    $" Requested version: '{desiredFirmwareVersion}'");

                ApplyFirmwareUpdate(desiredFirmwareVersion);
            }

            return Task.CompletedTask;
        }

        private static async Task ApplyFirmwareUpdate(string targetVersion)
        {
            Console.WriteLine("Beginning firmware update");

            _reportedProperties["firmwareUpdateStatus"] = $"Downloading zip file for firemware {targetVersion}...";
            await _device.UpdateReportedPropertiesAsync(_reportedProperties);
            Thread.Sleep(5000);

            _reportedProperties["firmwareUpdateStatus"] = $"Unzipping Package";
            await _device.UpdateReportedPropertiesAsync(_reportedProperties);
            Thread.Sleep(5000);

            _reportedProperties["firmwareUpdateStatus"] = $"Applying Update";
            await _device.UpdateReportedPropertiesAsync(_reportedProperties);
            Thread.Sleep(5000);

            Console.WriteLine("Firmware update complete!");

            _reportedProperties["firmwareUpdateStatus"] = "n/a";
            _reportedProperties["firmwareVersion"] = targetVersion;
            await _device.UpdateReportedPropertiesAsync(_reportedProperties);
        }
    }
}
