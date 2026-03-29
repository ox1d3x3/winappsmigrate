using System;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace AppMigrator.UI.Services;

public sealed class ConnectivitySnapshot
{
    public bool IsNetworkAvailable { get; init; }
    public bool HasInternetAccess { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class ConnectivityService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public async Task<ConnectivitySnapshot> GetStatusAsync(bool includeInternetProbe = false, CancellationToken cancellationToken = default)
    {
        var hasNetwork = NetworkInterface.GetIsNetworkAvailable()
            && NetworkInterface.GetAllNetworkInterfaces().Any(ni =>
                ni.OperationalStatus == OperationalStatus.Up
                && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

        if (!hasNetwork)
        {
            return new ConnectivitySnapshot
            {
                IsNetworkAvailable = false,
                HasInternetAccess = false,
                Summary = "Disconnected",
                Detail = "No active network adapter detected."
            };
        }

        if (!includeInternetProbe)
        {
            return new ConnectivitySnapshot
            {
                IsNetworkAvailable = true,
                HasInternetAccess = true,
                Summary = "Connected",
                Detail = "Active network adapter detected."
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.msftconnecttest.com/connecttest.txt");
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var online = response.IsSuccessStatusCode;
            return new ConnectivitySnapshot
            {
                IsNetworkAvailable = true,
                HasInternetAccess = online,
                Summary = online ? "Connected" : "Disconnected",
                Detail = online ? "Internet connection verified for package restore." : "Network is up, but internet could not be verified."
            };
        }
        catch
        {
            return new ConnectivitySnapshot
            {
                IsNetworkAvailable = true,
                HasInternetAccess = false,
                Summary = "Disconnected",
                Detail = "Network is up, but internet could not be verified."
            };
        }
    }
}
