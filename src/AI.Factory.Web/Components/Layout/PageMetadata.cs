namespace AI.Factory.Web.Components.Layout;

/// <summary>
/// Route to display name and workflow section, so the top bar can show where the user is
/// without every page having to declare it. Longest-prefix matched, which keeps detail routes
/// (/orders/5) under their parent's heading.
/// </summary>
public static class PageMetadata
{
    public sealed record Entry(string Section, string Title);

    private static readonly (string Route, Entry Meta)[] Routes =
    [
        ("/materials", new("Planning", "Material Management")),
        ("/orders", new("Planning", "Customer Orders")),
        // Longer prefix wins, so the create route keeps its own heading now that the page no
        // longer carries one.
        ("/orders/new", new("Planning", "New Customer Order")),
        ("/production-plans", new("Planning", "Production Plans")),
        ("/material-shortages", new("Procurement", "Material Shortage")),
        ("/procurement", new("Procurement", "Procurement")),
        ("/machine-monitoring", new("Operations", "Machine Monitoring")),
        ("/ai-copilot", new("Operations", "AI Copilot")),
        ("/audit-administration", new("Administration", "Audit & Administration")),
        ("/login", new("", "Sign in")),
        ("/error", new("", "Error")),
        ("/forbidden", new("", "Access denied")),
        ("/not-found", new("", "Not found")),
    ];

    private static readonly Entry Dashboard = new("Overview", "Dashboard");

    public static Entry For(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return Dashboard;
        }

        Entry? best = null;
        var bestLength = 0;
        foreach (var (route, meta) in Routes)
        {
            if (path.StartsWith(route, StringComparison.OrdinalIgnoreCase) && route.Length > bestLength)
            {
                best = meta;
                bestLength = route.Length;
            }
        }

        return best ?? Dashboard;
    }
}
