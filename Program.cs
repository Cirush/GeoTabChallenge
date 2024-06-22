using System.Text;
using Geotab.Checkmate;
using Geotab.Checkmate.ObjectModel;
using Geotab.Checkmate.ObjectModel.Engine;
using Geotab.Checkmate.Web;

if (args.Length != 4)
{
    Console.WriteLine();
    Console.WriteLine(" Command line parameters:");
    Console.WriteLine(" dotnet run 'server' 'database' 'username' 'password'");
    Console.WriteLine();
    Console.WriteLine(" Example: dotnet run 'server' 'database' 'username' 'password'");
    Console.WriteLine();
    Console.WriteLine(" server     - Sever host name (Example: my.geotab.com)");
    Console.WriteLine(" database   - Database name (Example: G560)");
    Console.WriteLine(" username   - Geotab user name");
    Console.WriteLine(" password   - Geotab password");
    return;
}

var server = args[0];
var database = args[1];
var username = args[2];
var password = args[3];

var tokenSource = new CancellationTokenSource();
var token = tokenSource.Token;

var api = await ApiAuthentication(server, database, username, password);

if (api.LoginResult == null)
{
    Console.WriteLine(" Could not Authenticate. End of program.");
    return;
}

var userInputTask = Task.Run(() =>
{
    Console.WriteLine();
    Console.WriteLine(" Running Vehicles Backup 🚗");
    Console.WriteLine(" Press any key to end the program...");
    Console.ReadKey(true);
    Console.WriteLine(" Finishing the program...");
    tokenSource.Cancel();
});

var vehicleBackupTask = Task.Run(async () =>
{
    while (!token.IsCancellationRequested)
    {
        try
        {
            await BackupVehicleData(api, token);
            await Task.Delay(10000, token);
        }
        catch (InvalidApiOperationException ex)
        {
            Console.WriteLine(ex.Message);
            return;
        }
        catch (WebServerInvokerJsonException ex)
        {
            Console.WriteLine(ex);
            await Task.Delay(10000, token);
        }
        catch(OverLimitException ex)
        {
            Console.WriteLine($" User has exceeded the query limit, retriying after a minute...{ex}");
            await Task.Delay(60000, token);
            Console.WriteLine(" Restarting backup...");
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return;
        }
    }
}, token);

await Task.WhenAny(userInputTask, vehicleBackupTask);
Console.WriteLine(" Program ended");

async Task BackupVehicleData(API api, CancellationToken token)
{
    var devices = await api.CallAsync<IList<Device>>("Get", typeof(Device));

    var calls = devices?.SelectMany(device => new[] { GetCoordinates(device), GetOdometer(device) }).ToArray();

    if (calls == null) return;

    var multiCallResults = await api.MultiCallAsync(calls);

    var deviceStatusInfoList = multiCallResults.OfType<IList<DeviceStatusInfo>>();
    var statusDataList = multiCallResults.OfType<IList<StatusData>>();

    var vehicleBackupList = new List<VehicleBackup>();

    for (int i = 0; i < devices?.Count; i++)
    {
        var device = devices[i];
        var goDevice = device as GoDevice;
        var statusInfo = deviceStatusInfoList.ElementAtOrDefault(i)?.FirstOrDefault();
        var statusData = statusDataList.ElementAtOrDefault(i)?.FirstOrDefault();

        if (device != null && statusInfo != null && statusData != null)
        {
            vehicleBackupList.Add(new VehicleBackup
            {
                Id = device.Id,
                Vin = goDevice?.VehicleIdentificationNumber,
                Latitude = statusInfo.Latitude,
                Longitude = statusInfo.Longitude,
                Odometer = statusData.Data
            });
        }
    }

    var directory = Path.Combine(Environment.CurrentDirectory, "VehiclesBackup");
    if (!Directory.Exists(directory))
    {
        Directory.CreateDirectory(directory);
    }

    await Parallel.ForEachAsync(vehicleBackupList, async (v, token) =>
    {
        var path = Path.Combine(directory, $"{v.Id}.csv");
        using (var writer = new StreamWriter(path, true))
        {
            await writer.WriteLineAsync(new StringBuilder().Append(v.ToCsv()), token);
        }
    });
}

async Task<API> ApiAuthentication(string server, string database, string username, string password)
{
    var api = new API(username, password, null, database, server);
    try
    {
        await api.AuthenticateAsync(token);
    }
    catch (InvalidUserException ex)
    {
        Console.WriteLine($" Invalid user: {ex}");
    }
    catch (DbUnavailableException ex)
    {
        Console.WriteLine($" Database unavailable: {ex}");
    }
    catch (OverLimitException ex)
    {
        Console.WriteLine($" User has exceeded the query limit: {ex}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($" Failed to authenticate user: {ex}");
    }
    return api;
}

static object[] GetCoordinates(Device device)
{
    return new object[]
    {
        "Get", typeof(DeviceStatusInfo), new
        {
            search = new DeviceStatusInfoSearch
            {
                DeviceSearch = new DeviceSearch
                {
                    Id = device.Id
                }
            },
            propertySelector = new PropertySelector
            {
                Fields = new List<string>
                {
                    nameof(DeviceStatusInfo.Latitude),
                    nameof(DeviceStatusInfo.Longitude)
                },
                IsIncluded = true
            }
        },
        typeof(IList<DeviceStatusInfo>)
    };
}

static object[] GetOdometer(Device device)
{
    return new object[]
    {
        "Get", typeof(StatusData), new
        {
            search = new StatusDataSearch
            {
                DeviceSearch = new DeviceSearch(device.Id),
                DiagnosticSearch = new DiagnosticSearch(KnownId.DiagnosticOdometerAdjustmentId),
                FromDate = DateTime.MaxValue
            },
            propertySelector = new PropertySelector
            {
                Fields = new List<string>
                {
                    nameof(StatusData.Data)
                },
                IsIncluded = true
            }
        },
        typeof(IList<StatusData>)
    };
}
