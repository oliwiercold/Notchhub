using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Xaml;
using System.Xml;

namespace NotchHub.UI.Modding;

/// <summary>
/// Restricted loader for widget views (view.xaml). Widget XAML is data, not code: the node stream is checked before any
/// object is created and the checked nodes (not the file again) are what gets loaded. Rejected: xmlns other than the WPF
/// presentation / xaml (and markup-compatibility / blend design) namespaces and clr-namespace:NotchHub.UI.*;assembly=NotchHub.UI,
/// types outside System.Windows.* (WPF assemblies) and the public NotchHub.UI view namespaces, data providers
/// (ObjectDataProvider, XmlDataProvider), navigation hosts (Frame, WebBrowser, Window), event handlers (incl. EventSetter),
/// x:Code / x:Class / x:FactoryMethod / x:Arguments, ResourceDictionary.Source (would load another file unchecked) and
/// Hyperlink.NavigateUri.
/// </summary>
internal static class WidgetXaml
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string MarkupCompat = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string Blend = "http://schemas.microsoft.com/expression/blend/2008";
    private const long MaxFileBytes = 1024 * 1024;

    private static readonly HashSet<string> WpfAssemblies = new(StringComparer.Ordinal)
    {
        "PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml",
    };

    // NotchHub.UI namespaces a view may use (Modding / Theming are app internals)
    private static readonly HashSet<string> UiNamespaces = new(StringComparer.Ordinal)
    {
        "NotchHub.UI", "NotchHub.UI.Controls", "NotchHub.UI.Converters", "NotchHub.UI.Animation", "NotchHub.UI.Localization",
    };

    private static readonly HashSet<string> ForbiddenDirectives = new(StringComparer.Ordinal)
    {
        "Code", "Class", "Subclass", "ClassModifier", "ClassAttributes", "FieldModifier", "FactoryMethod", "Arguments",
    };

    private static readonly Type[] ForbiddenTypes =
    [
        typeof(DataSourceProvider),      // ObjectDataProvider (calls any method), XmlDataProvider (loads any URL)
        typeof(Frame), typeof(WebBrowser), typeof(Window), typeof(NavigationService),
        typeof(EventSetter),             // event handler
    ];

    /// <summary>Checks the file and returns the view; throws <see cref="InvalidDataException"/> when it is not allowed.</summary>
    public static FrameworkElement Load(string path)
    {
        var nodes = Read(path);
        var root = System.Windows.Markup.XamlReader.Load(nodes.GetReader());
        return root as FrameworkElement ?? throw new InvalidDataException("view.xaml: the root element must be a FrameworkElement (e.g. Grid)");
    }

    /// <summary>Checks the file without creating anything; throws <see cref="InvalidDataException"/> when it is not allowed.</summary>
    public static void Validate(string path) => Read(path);

    private static XamlNodeList Read(string path)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("view not found", full);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("view.xaml: file larger than 1 MB");

        var schema = System.Windows.Markup.XamlReader.GetWpfSchemaContext();
        var ns = new XmlNamespaceManager(new NameTable());
        ns.AddNamespace(string.Empty, Presentation);
        ns.AddNamespace("x", XamlNs);
        var xmlSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var fs = File.OpenRead(full);
        using var xml = XmlReader.Create(fs, xmlSettings, new XmlParserContext(null, ns, null, XmlSpace.None));
        using var reader = new XamlXmlReader(xml, schema, new XamlXmlReaderSettings { BaseUri = new Uri(full), ProvideLineInfo = true });

        var nodes = new XamlNodeList(schema);
        var writer = nodes.Writer;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XamlNodeType.NamespaceDeclaration:
                    CheckNamespace(reader.Namespace.Namespace, reader);
                    break;
                case XamlNodeType.StartObject:
                    CheckType(reader.Type, reader);
                    break;
                case XamlNodeType.StartMember:
                    CheckMember(reader.Member, reader);
                    break;
            }
            writer.WriteNode(reader);
        }
        writer.Close();
        return nodes;
    }

    private static void CheckNamespace(string ns, System.Xaml.XamlReader at)
    {
        if (ns is Presentation or XamlNs or MarkupCompat or Blend) return;
        // clr-namespace:NotchHub.UI.Controls;assembly=NotchHub.UI
        var parts = ns.Split(';');
        if (parts.Length == 2
            && parts[0].StartsWith("clr-namespace:", StringComparison.Ordinal)
            && UiNamespaces.Contains(parts[0]["clr-namespace:".Length..].Trim())
            && parts[1].Trim() == "assembly=NotchHub.UI")
            return;
        Reject($"xmlns \"{ns}\" is not allowed (only the WPF namespaces and clr-namespace:NotchHub.UI.*;assembly=NotchHub.UI)", at);
    }

    private static void CheckType(XamlType? xt, System.Xaml.XamlReader at)
    {
        var t = xt?.UnderlyingType;
        if (xt is null || xt.IsUnknown || t is null) { Reject($"unknown type \"{xt?.Name}\"", at); return; }
        if (t.IsArray) t = t.GetElementType()!;
        var asm = t.Assembly.GetName().Name ?? "";
        var tns = t.Namespace ?? "";
        var allowed = (WpfAssemblies.Contains(asm) && (tns == "System.Windows" || tns.StartsWith("System.Windows.", StringComparison.Ordinal)))
                      || (asm == "NotchHub.UI" && UiNamespaces.Contains(tns));
        if (!allowed) Reject($"type \"{t.FullName}\" is not allowed in a widget view", at);
        foreach (var f in ForbiddenTypes)
            if (f.IsAssignableFrom(t)) Reject($"\"{t.Name}\" is not allowed in a widget view", at);
    }

    private static void CheckMember(XamlMember? m, System.Xaml.XamlReader at)
    {
        if (m is null) return;
        if (m.IsDirective)
        {
            if (ForbiddenDirectives.Contains(m.Name)) Reject($"x:{m.Name} is not allowed in a widget view", at);
            return;
        }
        if (m.IsUnknown) { Reject($"unknown property \"{m.Name}\"", at); return; }
        if (m.IsEvent) Reject($"event handler \"{m.Name}\" is not allowed in a widget view (use Command bindings)", at);
        CheckType(m.DeclaringType, at);   // attached properties of other types
        var owner = m.DeclaringType?.UnderlyingType;
        if (owner is null) return;
        if (m.Name == "Source" && typeof(ResourceDictionary).IsAssignableFrom(owner))
            Reject("ResourceDictionary.Source is not allowed in a widget view (keep resources inline)", at);
        if (m.Name == "NavigateUri" && typeof(Hyperlink).IsAssignableFrom(owner))
            Reject("Hyperlink.NavigateUri is not allowed in a widget view", at);
    }

    private static void Reject(string message, System.Xaml.XamlReader at)
    {
        var line = at is IXamlLineInfo { HasLineInfo: true } li ? $" (line {li.LineNumber})" : "";
        throw new InvalidDataException("view.xaml: " + message + line);
    }
}
