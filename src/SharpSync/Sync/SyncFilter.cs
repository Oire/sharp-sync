using System.Text.RegularExpressions;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Default implementation of sync filter with pattern matching
/// </summary>
public class SyncFilter : ISyncFilter
{
    private readonly List<string> _excludePatterns = new();
    private readonly List<string> _includePatterns = new();
    private readonly List<Regex> _excludeRegexes = new();
    private readonly List<Regex> _includeRegexes = new();

    /// <summary>
    /// Determines whether a file or directory should be synchronized
    /// </summary>
    public bool ShouldSync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Normalize path separators
        path = path.Replace('\\', '/').Trim('/');

        // If we have include patterns, the path must match at least one
        if (_includePatterns.Count > 0 || _includeRegexes.Count > 0)
        {
            bool included = false;
            
            // Check simple patterns
            foreach (var pattern in _includePatterns)
            {
                if (MatchesWildcard(path, pattern))
                {
                    included = true;
                    break;
                }
            }

            // Check regex patterns if not already included
            if (!included)
            {
                foreach (var regex in _includeRegexes)
                {
                    if (regex.IsMatch(path))
                    {
                        included = true;
                        break;
                    }
                }
            }

            if (!included)
                return false;
        }

        // Check exclude patterns
        foreach (var pattern in _excludePatterns)
        {
            if (MatchesWildcard(path, pattern))
                return false;
        }

        // Check regex excludes
        foreach (var regex in _excludeRegexes)
        {
            if (regex.IsMatch(path))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Adds an exclusion pattern
    /// </summary>
    public void AddExclusionPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return;

        pattern = pattern.Replace('\\', '/').Trim('/');

        // If it looks like a regex (contains regex special chars), compile it
        if (IsRegexPattern(pattern))
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                _excludeRegexes.Add(regex);
            }
            catch
            {
                // If regex compilation fails, treat as wildcard
                _excludePatterns.Add(pattern);
            }
        }
        else
        {
            _excludePatterns.Add(pattern);
        }
    }

    /// <summary>
    /// Adds an inclusion pattern
    /// </summary>
    public void AddInclusionPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return;

        pattern = pattern.Replace('\\', '/').Trim('/');

        // If it looks like a regex, compile it
        if (IsRegexPattern(pattern))
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                _includeRegexes.Add(regex);
            }
            catch
            {
                // If regex compilation fails, treat as wildcard
                _includePatterns.Add(pattern);
            }
        }
        else
        {
            _includePatterns.Add(pattern);
        }
    }

    /// <summary>
    /// Clears all patterns
    /// </summary>
    public void Clear()
    {
        _excludePatterns.Clear();
        _includePatterns.Clear();
        _excludeRegexes.Clear();
        _includeRegexes.Clear();
    }

    /// <summary>
    /// Creates a filter from common exclusion patterns
    /// </summary>
    public static SyncFilter CreateDefault()
    {
        var filter = new SyncFilter();
        
        // Version control
        filter.AddExclusionPattern(".git");
        filter.AddExclusionPattern(".svn");
        filter.AddExclusionPattern(".hg");
        
        // Build outputs
        filter.AddExclusionPattern("bin");
        filter.AddExclusionPattern("obj");
        filter.AddExclusionPattern("target");
        filter.AddExclusionPattern("dist");
        filter.AddExclusionPattern("build");
        
        // Package managers
        filter.AddExclusionPattern("node_modules");
        filter.AddExclusionPattern("packages");
        filter.AddExclusionPattern("vendor");
        
        // IDE files
        filter.AddExclusionPattern(".vs");
        filter.AddExclusionPattern(".idea");
        filter.AddExclusionPattern(".vscode");
        filter.AddExclusionPattern("*.suo");
        filter.AddExclusionPattern("*.user");
        
        // Temporary files
        filter.AddExclusionPattern("*.tmp");
        filter.AddExclusionPattern("*.temp");
        filter.AddExclusionPattern("~*");
        filter.AddExclusionPattern("*~");
        filter.AddExclusionPattern("#*#");
        
        // OS files
        filter.AddExclusionPattern(".DS_Store");
        filter.AddExclusionPattern("Thumbs.db");
        filter.AddExclusionPattern("desktop.ini");
        
        return filter;
    }

    private bool MatchesWildcard(string path, string pattern)
    {
        // Handle directory patterns (ending with /)
        if (pattern.EndsWith('/'))
        {
            pattern = pattern.TrimEnd('/');
            // Check if path is or is under this directory
            return path.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase);
        }

        // Convert wildcard to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*") // ** matches any number of directories
            .Replace("\\*", "[^/]*") // * matches any characters except /
            .Replace("\\?", "[^/]")  // ? matches single character except /
            + "$";

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
    }

    private bool IsRegexPattern(string pattern)
    {
        // Check for common regex special characters
        return pattern.Contains('^') || pattern.Contains('$') || 
               pattern.Contains('[') || pattern.Contains(']') ||
               pattern.Contains('(') || pattern.Contains(')') ||
               pattern.Contains('+') || pattern.Contains('{') ||
               pattern.Contains('|') || pattern.Contains('\\');
    }
}