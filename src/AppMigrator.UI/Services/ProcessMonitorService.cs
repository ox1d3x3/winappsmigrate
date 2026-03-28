using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Services;

public sealed class ProcessMonitorService
{
    private readonly KnownRuleRepository _ruleRepository;

    public ProcessMonitorService(KnownRuleRepository ruleRepository)
    {
        _ruleRepository = ruleRepository;
    }

    public IReadOnlyList<string> GetRunningProcesses(DiscoveredApp app)
    {
        var rule = _ruleRepository.GetById(app.RuleId);
        var processNames = rule?.ProcessNames ?? new List<string>();
        return GetRunningProcesses(processNames);
    }

    public IReadOnlyList<string> GetRunningProcesses(IEnumerable<string> processNames)
    {
        var running = new List<string>();
        foreach (var processName in processNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? processName[..^4]
                    : processName;
                if (Process.GetProcessesByName(normalized).Any())
                {
                    running.Add(normalized);
                }
            }
            catch
            {
            }
        }

        return running;
    }

    public async Task<bool> WaitForProcessesToExitAsync(IEnumerable<string> processNames, TimeSpan timeout)
    {
        var stopAt = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < stopAt)
        {
            if (GetRunningProcesses(processNames).Count == 0)
            {
                return true;
            }

            await Task.Delay(1000);
        }

        return GetRunningProcesses(processNames).Count == 0;
    }
}
