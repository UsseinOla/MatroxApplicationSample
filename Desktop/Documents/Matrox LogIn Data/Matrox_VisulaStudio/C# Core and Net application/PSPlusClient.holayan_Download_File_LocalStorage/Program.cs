
namespace PSPlusClient.HelloWorld
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Nito.AsyncEx;
    using SV2Client;
    using System.Collections.Generic;

    /// <summary>Class containing the program entry point. This program simply demonstrates how to go about
    /// changing the friendly name of a Maevex unit.</summary>
    internal class Program
    {
        /// <summary>Program entry point.</summary>
        /// <param name="args">Command-line arguments.</param>
        private static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Sample program adding \" Hello, World!\" to the friendly name of the device.");
                Console.WriteLine();
                Console.WriteLine("Using SV2Client.dll version " + Assembly.LoadFrom("SV2Client.dll").GetName().Version.ToString());
                Console.WriteLine();
                Console.WriteLine("Program usage:");
                Console.WriteLine(" PSPlusClient.HelloWorld urn=<urn> uri=<uri> username=<user> password=<pw>");
                Console.WriteLine();
                Console.WriteLine("Where:");
                Console.WriteLine(" <urn> is the URN of the device.");
                Console.WriteLine(" <uri> is the URI of the device.");
                Console.WriteLine(" <user> is the optional user name credential required to make changes to the device.");
                Console.WriteLine(" <pw> is the user password credential required to make changes to the device.");
                Console.WriteLine();
                Console.WriteLine("Example, the device is a Maevex 5100-series encoder with a URI of 192.168.165.179,");
                Console.WriteLine("a user name of matrox and a password of matrox12345.");
                Console.WriteLine();
                Console.WriteLine(" PSPlusClient.HelloWorld urn=Maevex1 uri=192.168.165.179 username=matrox password=matrox12345");
                Console.WriteLine();
                Console.WriteLine("Example, assuming the device is a Maevex 6100-series encoder with a URI of 192.168.165.101,");
                Console.WriteLine("a user name of matrox and a password of matrox12345.");
                Console.WriteLine();
                Console.WriteLine(" PSPlusClient.HelloWorld urn=SV2 uri=192.168.165.101 username=matrox password=matrox12345");
                Console.WriteLine();
                Console.WriteLine("Example, assuming the device is a Maevex 6100-series encoder with an IPv6 address of fe80::220:fcff:fe32:480,");
                Console.WriteLine("a username of matrox and a password of matrox12345.");
                Console.WriteLine();
                Console.WriteLine("PSPlusClient.HelloWorld urn=SV2 uri=[fe80::220:fcff:fe32:480] username=matrox password=matrox12345");
                Console.WriteLine();
                Console.WriteLine("NOTE: If the device uses IPv6 address, the URI must be enclosed by square brackets ([\"IPv6 address\"]).");
                Console.WriteLine();
                Console.WriteLine("Example, assuming the device is a Maevex 6152-series decoder with a URI of 192.168.165.102,");
                Console.WriteLine("a user name of matrox and a password of matrox12345.");
                Console.WriteLine();
                Console.WriteLine(" PSPlusClient.HelloWorld urn=SV2Dec uri=192.168.165.102 username=matrox password=matrox12345");
                Console.WriteLine();

                return;
            }

            // Communication with Maevex5100 and Maevex6100 devices requires TLS 1.0 and TLS 1.2 respectively.
            System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls | System.Net.SecurityProtocolType.Tls12;

            //override

            UserRequest request = Program.ParseCommandLine(args);

            if (Program.ValidateUserRequest(request))
            {
                // Create async pump - console apps have no pump mechanism for dispatching between threads
                // (unlike windowed apps).
                AsyncContext.Run(async () =>
                {
                    await Program.ExecuteRequestAsync(request).ConfigureAwait(false);

                    // Dispose the unit access instance. This ensures all processing related to monitoring is
                    // completed before the application exits.
                    UnitAccess.Instance.Dispose();
                });
            }
        }

        /// <summary>Change the friendly name of the device as specified by the passed-in request. Both
        /// Maevex 5100-series and Maevex 6100-series are supported.</summary>
        /// <param name="request">Object containing user request details.</param>
        /// <returns>An awaitable task representing the asynchronous operation.</returns>
        private static async Task ExecuteRequestAsync(UserRequest request)
        {
            // UnitAccess is the entry point to the library. The singleton instance implements IDisposable
            // so don't forget to call it before exiting the app.
            // The Monitor property gives access to the units monitor, which finds and monitors all units on
            // the network.
            // To get monitoring under way for just the unit you want to test,
            // first get the monitor instance through UnitAccess.Instance.Monitor.
            var monitor = UnitAccess.Instance.Monitor;

            // TryAddUsingAsync simply sends a request to the specified IP - no guarantee it will ever answer back.
            // This is why you should provide a time-out in the cancellation token.
            bool additionSucceeded = false;
            var deviceURI = new UnitUri("https://" + request.DeviceURI);

            using (var timeoutCancelTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                // Call TryAddUnitAsync with the info about the board you???re testing against.
                additionSucceeded = await monitor.TryAddUnitAsync(
                                        request.DeviceURN,
                                        deviceURI,
                                        timeoutCancelTokenSource.Token).ConfigureAwait(false);
            }

            // On success, the board will be in the monitor???s Units collection of unit ???snapshots???.
            // These snapshots represent info and settings about a given board at a point in time.
            if (additionSucceeded)
            {
                // Wait for the unit you???re looking for to be reported as discovered, and a context
                // has been successfully retrieved
                UnitSnapshot unit = null;

                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                    unit = monitor.Units.FirstOrDefault(m => m.UnitUri == deviceURI);
                }
                while (unit?.Context == null);

                // Handle Maevex 6100-series encoders first.

                if ((unit.Urn == UnitURN.SV2Dec) && (unit.Capabilities == UnitCapabilities.CanDecode))
                {
                    // This is a Maevex 6100-series decoder.
                    // The "Environment password" is a Maevex 5100-series only concept. With Maevex 6100-series, each user has its own password)
                    // The device information is available to all, but you must provide the correct username and password
                    // to control the device (reboot or apply new settings).
                    SV2Client.Credentials credentials = new SV2Client.Credentials();
                    credentials.UserName = request.UserName;
                    credentials.Password = request.Password;
                    UnitAccess.Instance.SV2Credentials = credentials;

                    // Once you found the latest snapshot of your board in the Units,
                    // its context is found in the Context property of the snapshot.
                    var context = unit.Context as SV2.Types.Decoder.Context;

                    // Get the settings (from the latest context).
                    // To make sure it???s not affected by another device update being received, do a
                    // DeepClone() of the settings, then use this copy from now on in this apply sequence.
                    var settings = context.Settings.DeepClone() as SV2.Types.Decoder.ContextDetails.Settings;

                    // Output the current friendly name of the device.
                    Console.WriteLine("The device's friendly name is " + settings.FriendlyName);

                    // Change the device's friendly name.
                    settings.FriendlyName += " Hello, World!";

                    // To make an action on a device(reboot or apply settings),
                    // use the correct device controller (SV2DecController in this case).
                    var controller = UnitAccess.Instance.SV2DecController;

                    // Get the MarkMap (from the latest context).
                    // To make sure it???s not affected by another device update being received, do a
                    // DeepClone() of the MarkMap, then use this copy from now on in this apply sequence.
                    var markMap = context.MarkMap.DeepClone() as SV2.Types.Decoder.ContextDetails.MarkMap;

                    // vp download a vedio in local storage area Decoder to start the test
                    // Filling MediaInformationLocalStorage struct
                    // Filling the LocalFilePath string
                    // Filling up the FileDownloadInfo struct
                    // Calling DownLoadLocalFileAsync() API
                    // Checking Media Information Local Storage

                    // Note: the file path should contain at the end an addition defulat string name that 
                    // helps the computer recognises our specific direction
                    // the max file size should be 2073741824
                    // After downloading the file the file should be opened by VLC media player 

                    var Media = new SV2.Types.Decoder.ContextDetails.MediaInformationLocalStorage();

                    Media.Uri = new System.Uri("file://localhost/failsafe/medias/NewYork");
                    Media.FileSize = 2073741824; 
                    Media.IsComplete = true;

                    string localdFilePath = @"C:/Users/holayan/Desktop/HusseinTest"; 

                    List<SV2Client.SV2DecController.FileDownloadInfo> list = new List<SV2DecController.FileDownloadInfo>();

                    var fileDownload = new SV2Client.SV2DecController.FileDownloadInfo(Media, localdFilePath);

                    list.Add(fileDownload);

                    var resultTuple = await controller.DownloadLocalFilesAsync(
                        unit.Uuid,
                        unit.UnitUri,
                        list,
                        1000000000,
                        null,
                        CancellationToken.None);

                }
            }
            else
            {
                Console.WriteLine("The unit was not successfully added.");
            }
        }

        /// <summary>Parse the passed-in command-line arguments, figuring out the user request.</summary>
        /// <param name="args">The command-line arguments.</param>
        /// <returns>Object representing the parsed command-line arguments.</returns>
        private static UserRequest ParseCommandLine(string[] args)
        {
            var request = new UserRequest();
            char[] separators = new char[] { '=' };

            foreach (string arg in args)
            {
                string[] parts = arg.Trim().Split(separators);

                switch (parts.Length)
                {
                    case 2:
                        switch (parts[0].ToUpperInvariant())
                        {
                            case "URN":
                                if (parts[1].ToUpperInvariant() == "MAEVEX1")
                                {
                                    request.DeviceURN = UnitURN.Maevex1;
                                }
                                else if (parts[1].ToUpperInvariant() == "SV2")
                                {
                                    request.DeviceURN = UnitURN.SV2;
                                }
                                else if (parts[1].ToUpperInvariant() == "SV2DEC")
                                {
                                    request.DeviceURN = UnitURN.SV2Dec;
                                }
                                break;

                            case "URI":
                                var address = parts[1].TrimStart('[').TrimEnd(']');

                                request.DeviceURI = address.Contains(":") ? "[" + address + "]" : address;
                                break;

                            case "USERNAME":
                                request.UserName = parts[1];
                                break;

                            case "PASSWORD":
                                request.Password = parts[1];
                                break;

                            default:
                                Console.WriteLine("Unknown parameter name: " + parts[0]);
                                break;
                        }

                        break;

                    default:
                        Console.WriteLine("Invalid parameter: " + arg);
                        break;
                }
            }

            return request;
        }

        /// <summary>Validate the parsed user request, ensuring all required elements are present and they
        /// represent a coherent operation to perform.</summary>
        /// <param name="request">The request to validate.</param>
        /// <returns>True if the request seems valid; otherwise false.</returns>
        private static bool ValidateUserRequest(UserRequest request)
        {
            // User must specify the device URN.
            if (string.IsNullOrWhiteSpace(request.DeviceURN))
            {
                Console.WriteLine("An invalid URN was specified.");
                return false;
            }

            // User must specify the device's URI.
            if (string.IsNullOrWhiteSpace(request.DeviceURI))
            {
                Console.WriteLine("Device URI not specified.");
                return false;
            }

            // The user must specify the user name.
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                Console.WriteLine("User name credential not specified.");
                return false;
            }

            // The user must specify the network share user password.
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                Console.WriteLine("Password credential not specified.");
                return false;
            }

            return true;
        }

        /// <summary>Represents the command-line user request once parsed from the command-line arguments.
        /// </summary>
        private class UserRequest
        {
            /// <summary>Initializes a new instance of the <see cref="UserRequest"/> class.</summary>
            public UserRequest()
            {
            }

            /// <summary>Gets or sets the device URN.</summary>
            public string DeviceURN { get; set; }

            /// <summary>Gets or sets the device URI.</summary>
            public string DeviceURI { get; set; }

            /// <summary>Gets or sets the user name credential.</summary>
            public string UserName { get; set; }

            /// <summary>Gets or sets the password credential.</summary>
            public string Password { get; set; }
        }
    }
}
