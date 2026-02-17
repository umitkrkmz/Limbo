using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Limbo.Core.Analysis;

/// <summary>
/// Manages auto-detected blocked domains (Radar System).
/// Saves the data as Auto-Blacklist.txt in the application directory.
/// </summary>
public class BlacklistManager
{
    private readonly HashSet<string> _blacklistedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private readonly object _lock = new();

    // Event triggered when the radar detects a new blocked domain on-the-fly
    public event Action<string>? OnNewBlacklistedDomain;

    public BlacklistManager(string? filePath = null)
    {
        if (filePath != null && Path.IsPathRooted(filePath))
        {
            // Full path provided directly (used in tests or custom setups)
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            _filePath = filePath;
        }
        else
        {
            // Default: %LOCALAPPDATA%\Limbo\<fileName>
            var fileName = filePath ?? "Auto-Blacklist.txt";
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var limboDir = Path.Combine(appData, "Limbo");
            Directory.CreateDirectory(limboDir);
            _filePath = Path.Combine(limboDir, fileName);
        }

        LoadExisting();
    }

    /// <summary>
    /// Loads the existing blacklist from the disk into memory.
    /// </summary>
    private void LoadExisting()
    {
        if (!File.Exists(_filePath)) return;

        var domains = File.ReadAllLines(_filePath);
        foreach (var domain in domains)
        {
            var cleanDomain = domain.Trim();
            if (!string.IsNullOrWhiteSpace(cleanDomain))
            {
                _blacklistedDomains.Add(cleanDomain);
            }
        }
    }

    /// <summary>
    /// Adds a newly detected domain to the blacklist.
    /// Appends to the file for performance during live capture.
    /// </summary>
    public void AddToBlacklist(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || System.Net.IPAddress.TryParse(domain, out _)) 
            return;

        lock (_lock)
        {
            if (_blacklistedDomains.Add(domain))
            {
                // Append only the new domain to the file (faster than rewriting the whole file during active capture)
                File.AppendAllText(_filePath, $"{domain}{Environment.NewLine}");
                
                // Notify the engine to apply an Adaptive profile immediately
                OnNewBlacklistedDomain?.Invoke(domain);
            }
        }
    }

    /// <summary>
    /// Imports an external blacklist file.
    /// Overwrites (Write) the current in-memory list and the original file completely.
    /// </summary>
    public void Import(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Source blacklist file not found.", sourceFilePath);

        var newDomains = File.ReadAllLines(sourceFilePath);
        
        lock (_lock)
        {
            // Clear current memory state
            _blacklistedDomains.Clear();
            
            foreach (var domain in newDomains)
            {
                var cleanDomain = domain.Trim();
                if (!string.IsNullOrWhiteSpace(cleanDomain))
                {
                    _blacklistedDomains.Add(cleanDomain);
                }
            }
            
            // Overwrite the original Auto-Blacklist.txt completely (Write, not Append)
            File.WriteAllLines(_filePath, _blacklistedDomains);
        }
    }

    /// <summary>
    /// Exports the current blacklist to a specified destination path.
    /// </summary>
    public void Export(string destinationFilePath)
    {
        lock (_lock)
        {
            // Write the current clean memory state directly to the target export file
            File.WriteAllLines(destinationFilePath, _blacklistedDomains);
        }
    }

    /// <summary>
    /// Returns a thread-safe snapshot of all currently blacklisted domains.
    /// </summary>
    public IReadOnlyCollection<string> GetAllBlacklisted() 
    {
        lock (_lock)
        {
            return _blacklistedDomains.ToList();
        }
    }
}