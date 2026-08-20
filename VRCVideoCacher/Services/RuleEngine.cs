using System.Text.RegularExpressions;
using System.Web;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

public class RuleEvaluationResult
{
    public UriRule MatchedRule { get; set; } = null!;
    public string FinalUrl { get; set; } = string.Empty;
    public RuleAction Action { get; set; } = RuleAction.Direct;
    public int? MaxResolution { get; set; }
    public int? MaxDurationMinutes { get; set; }
    public string RedirectUrl { get; set; } = string.Empty;
}

public static class RuleEngine
{
    private static readonly Serilog.ILogger Log = Program.Logger.ForContext(typeof(RuleEngine));

    public static RuleEvaluationResult EvaluateUrl(string requestUrl)
    {
        var currentUrl = requestUrl.Trim();
        var rules = ConfigManager.Config.UriRules;

        if (rules == null || rules.Count == 0)
        {
            rules = ConfigModel.GetDefaultRules();
        }

        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
                continue;

            try
            {
                var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
                var match = regex.Match(currentUrl);

                if (!match.Success)
                    continue;

                Log.Information("URL '{URL}' matched rule '{RuleName}' ({Action})", currentUrl, rule.Name, rule.Action);

                var result = new RuleEvaluationResult
                {
                    MatchedRule = rule,
                    Action = rule.Action,
                    FinalUrl = currentUrl,
                    MaxResolution = rule.MaxResolution,
                    MaxDurationMinutes = rule.MaxDurationMinutes
                };

                switch (rule.Action)
                {
                    case RuleAction.Rewrite:
                        var expandedRewrite = ExpandTemplate(rule.RedirectTarget, currentUrl, match);
                        Log.Information("Rule '{RuleName}' rewritten URL: '{Original}' -> '{Rewritten}'", rule.Name, currentUrl, expandedRewrite);
                        currentUrl = expandedRewrite;
                        // Continue loop so lower rules evaluate against the rewritten URL
                        break;

                    case RuleAction.Redirect:
                        var expandedRedirect = ExpandTemplate(rule.RedirectTarget, currentUrl, match);
                        Log.Information("Rule redirect expanded: '{Target}'", expandedRedirect);
                        result.RedirectUrl = expandedRedirect;
                        result.FinalUrl = expandedRedirect;
                        return result;

                    case RuleAction.Block:
                    case RuleAction.Cache:
                    case RuleAction.Direct:
                    default:
                        result.FinalUrl = currentUrl;
                        return result;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error evaluating rule pattern '{Pattern}' for URL '{URL}'", rule.Pattern, currentUrl);
            }
        }

        // Fallback default if no rule matched
        return new RuleEvaluationResult
        {
            MatchedRule = new UriRule { Name = "Everything else", Action = RuleAction.Direct },
            Action = RuleAction.Direct,
            FinalUrl = currentUrl
        };
    }

    public static string ExpandTemplate(string template, string originalUrl, Match regexMatch)
    {
        if (string.IsNullOrEmpty(template))
            return originalUrl;

        var result = template;

        // 1. First substitute regex capture groups ($0, $1, $2, etc.)
        if (regexMatch.Success)
        {
            result = regexMatch.Result(result);
        }

        // 2. Parse URL for token substitution {url...}
        Uri? uri = null;
        try
        {
            Uri.TryCreate(originalUrl, UriKind.Absolute, out uri);
        }
        catch
        {
            // ignored
        }

        // Token replacements
        result = result.Replace("{url}", originalUrl);
        result = result.Replace("{url.raw}", originalUrl);
        result = result.Replace("{url.full}", originalUrl);

        if (uri != null)
        {
            result = result.Replace("{url.scheme}", uri.Scheme);
            result = result.Replace("{url.host}", uri.Host);
            result = result.Replace("{url.domain}", uri.Host);
            result = result.Replace("{url.port}", uri.Port.ToString());
            result = result.Replace("{url.path}", uri.AbsolutePath);
            result = result.Replace("{url.query}", uri.Query);
            result = result.Replace("{url.authority}", uri.Authority);
            result = result.Replace("{url.fragment}", uri.Fragment.TrimStart('#'));
            result = result.Replace("{url.hash}", uri.Fragment.TrimStart('#'));

            // Handle {url.query.PARAM} replacements
            if (result.Contains("{url.query."))
            {
                var queryParams = HttpUtility.ParseQueryString(uri.Query);
                var tokenRegex = new Regex(@"\{url\.query\.([a-zA-Z0-9_\-]+)\}");
                result = tokenRegex.Replace(result, m =>
                {
                    var paramName = m.Groups[1].Value;
                    var paramVal = queryParams[paramName];
                    return paramVal ?? string.Empty;
                });
            }
        }

        return result;
    }
}
