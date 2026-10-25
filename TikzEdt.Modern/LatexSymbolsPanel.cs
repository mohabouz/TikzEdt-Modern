using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TikzEdt.Modern;

internal sealed record LatexSymbol(string Category, string Glyph, string Command)
{
    public string Meaning => SymbolSearch.Meaning(Command);
    public string? Package => SymbolSearch.Package(Command);
}
internal static class LatexSymbolCatalog
{
    public static IReadOnlyList<LatexSymbol> Items { get; } = Build();
    private static List<LatexSymbol> Build()
    {
        var result = new List<LatexSymbol>();
        void Group(string category, string entries)
        {
            foreach (var entry in entries.Split('|')) { var pair = entry.Split('~', 2); result.Add(new(category, pair[0], pair[1])); }
        }
        Group("Greek — lowercase", @"α~\alpha|β~\beta|γ~\gamma|δ~\delta|ε~\epsilon|ϵ~\varepsilon|ζ~\zeta|η~\eta|θ~\theta|ϑ~\vartheta|ι~\iota|κ~\kappa|λ~\lambda|μ~\mu|ν~\nu|ξ~\xi|π~\pi|ϖ~\varpi|ρ~\rho|ϱ~\varrho|σ~\sigma|ς~\varsigma|τ~\tau|υ~\upsilon|φ~\phi|ϕ~\varphi|χ~\chi|ψ~\psi|ω~\omega");
        Group("Greek — uppercase", @"Γ~\Gamma|Δ~\Delta|Θ~\Theta|Λ~\Lambda|Ξ~\Xi|Π~\Pi|Σ~\Sigma|Υ~\Upsilon|Φ~\Phi|Ψ~\Psi|Ω~\Omega");
        Group("Relations", @"=~=|≠~\ne|≈~\approx|≃~\simeq|≅~\cong|≡~\equiv|∼~\sim|∝~\propto|<~<|>~>|≤~\leq|≥~\geq|≪~\ll|≫~\gg|⊥~\perp|∥~\parallel|≍~\asymp|≺~\prec|≻~\succ|≼~\preceq|≽~\succeq|≐~\doteq");
        Group("Operators", @"+~+|−~-|±~\pm|∓~\mp|×~\times|÷~\div|⋅~\cdot|∘~\circ|⋆~\star|∗~\ast|⊕~\oplus|⊗~\otimes|⊙~\odot|⊖~\ominus|⊘~\oslash|∧~\wedge|∨~\vee|†~\dagger|‡~\ddagger");
        Group("Calculus and large operators", @"∑~\sum|∏~\prod|∐~\coprod|∫~\int|∬~\iint|∭~\iiint|∮~\oint|∂~\partial|∇~\nabla|∞~\infty|√~\sqrt{}|lim~\lim|sup~\sup|inf~\inf|max~\max|min~\min|⋃~\bigcup|⋂~\bigcap|⋁~\bigvee|⋀~\bigwedge");
        Group("Sets and logic", @"∈~\in|∉~\notin|∋~\ni|⊂~\subset|⊃~\supset|⊆~\subseteq|⊇~\supseteq|∪~\cup|∩~\cap|∅~\emptyset|∀~\forall|∃~\exists|∄~\nexists|¬~\neg|∴~\therefore|∵~\because|⊢~\vdash|⊨~\models|⊤~\top|⊥~\bot|ℕ~\mathbb{N}|ℤ~\mathbb{Z}|ℚ~\mathbb{Q}|ℝ~\mathbb{R}|ℂ~\mathbb{C}|℘~\mathcal{P}|∖~\setminus");
        Group("Arrows", @"←~\leftarrow|→~\rightarrow|↔~\leftrightarrow|↑~\uparrow|↓~\downarrow|↕~\updownarrow|⇐~\Leftarrow|⇒~\Rightarrow|⇔~\Leftrightarrow|⇑~\Uparrow|⇓~\Downarrow|⇕~\Updownarrow|⟵~\longleftarrow|⟶~\longrightarrow|⟷~\longleftrightarrow|↦~\mapsto|↪~\hookrightarrow|↩~\hookleftarrow|↗~\nearrow|↘~\searrow|↙~\swarrow|↖~\nwarrow|⇌~\rightleftharpoons");
        Group("Brackets and structures", @"( )~\left( \right)|[ ]~\left[ \right]|{ }~\left\{ \right\}|⟨ ⟩~\langle \rangle|⌈ ⌉~\lceil \rceil|⌊ ⌋~\lfloor \rfloor|a/b~\frac{}{}|xⁿ~^{}|xₙ~_{}|√x~\sqrt{}|∛x~\sqrt[3]{}");
        result.Add(new("Brackets and structures", "∣ ∣", @"\left| \right|"));
        result.Add(new("Brackets and structures", "‖ ‖", @"\left\| \right\|"));
        Group("Accents", @"â~\hat{}|ā~\bar{}|ã~\tilde{}|ȧ~\dot{}|ä~\ddot{}|a⃗~\vec{}|âb~\widehat{}|ãb~\widetilde{}|a̅~\overline{}|a̲~\underline{}");
        Group("Geometry and miscellaneous", @"∠~\angle|∡~\measuredangle|△~\triangle|□~\square|◇~\diamond|°~^{\circ}|ℓ~\ell|ℏ~\hbar|ℜ~\Re|ℑ~\Im|ℵ~\aleph|℧~\mho|′~\prime|…~\ldots|⋯~\cdots|⋮~\vdots|⋱~\ddots|♭~\flat|♮~\natural|♯~\sharp|∥~\Vert");
        return result;
    }
    public static bool IsMathContext(string source, int offset)
    {
        var prefix = Regex.Replace(source[..Math.Clamp(offset, 0, source.Length)], @"(?m)(?<!\\)%.*$", "");
        var dollars = Regex.Matches(prefix, @"(?<!\\)\${1,2}");
        if (dollars.Count % 2 == 1) return true;
        if (prefix.LastIndexOf(@"\(", StringComparison.Ordinal) > prefix.LastIndexOf(@"\)", StringComparison.Ordinal) || prefix.LastIndexOf(@"\[", StringComparison.Ordinal) > prefix.LastIndexOf(@"\]", StringComparison.Ordinal)) return true;
        return Regex.Matches(prefix, @"\\begin\{(?:equation|align|gather|multline|math|displaymath)\*?\}").Count > Regex.Matches(prefix, @"\\end\{(?:equation|align|gather|multline|math|displaymath)\*?\}").Count;
    }
}

internal sealed class LatexSymbolsPanel : DockPanel
{
    public event Action<string>? InsertRequested;
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(6), ToolTip = "Search symbols or LaTeX commands" };
    private readonly ComboBox _category = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly WrapPanel _items = new();
    private readonly SymbolPreferences _preferences = SymbolPreferences.Load();
    public LatexSymbolsPanel()
    {
        Margin = new Thickness(6);
        var heading = new StackPanel(); SetDock(heading, Dock.Top); heading.Children.Add(_search); heading.Children.Add(_category); Children.Add(heading);
        var help = new TextBlock { Text = "Click to insert · right-click to favorite. Search by meaning or command; hover for package requirements.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 6, 0, 3) }; SetDock(help, Dock.Bottom); Children.Add(help);
        Children.Add(new ScrollViewer { Content = _items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _category.ItemsSource = new[] { "All categories", "Favorites", "Recent" }.Concat(LatexSymbolCatalog.Items.Select(item => item.Category).Distinct()).ToArray();
        _search.MaxLength = 150;
        _search.TextChanged += (_, _) => Refresh(); _category.SelectionChanged += (_, _) => Refresh(); _category.SelectedIndex = 0;
    }
    private void Refresh()
    {
        _items.Children.Clear();
        var category = _category.SelectedItem?.ToString();
        var candidates = LatexSymbolCatalog.Items.Where(item => category switch { "All categories" => true, "Favorites" => _preferences.Favorites.Contains(item.Command), "Recent" => _preferences.Recent.Contains(item.Command), _ => item.Category == category });
        var matches = candidates.Select(s => (Symbol: s, Score: SymbolSearch.Score(_search.Text, s))).Where(s => s.Score >= 0).OrderByDescending(s => s.Score).ThenBy(s => category == "Recent" ? _preferences.Recent.IndexOf(s.Symbol.Command) : 0);
        foreach (var match in matches)
        {
            var symbol = match.Symbol;
            var button = new Button { Content = new TextBlock { Text = symbol.Glyph, FontFamily = new FontFamily("Cambria Math"), FontSize = 21, HorizontalAlignment = HorizontalAlignment.Center }, Width = 43, Height = 39, Padding = new Thickness(2), Margin = new Thickness(2), ToolTip = MainWindow.RichToolTip(symbol.Glyph + (_preferences.Favorites.Contains(symbol.Command) ? " ★" : ""), symbol.Command + "\n" + symbol.Meaning + "\n" + (symbol.Package is { } package ? "Requires \\usepackage{" + package + "}" : "Standard LaTeX math")) };
            button.Click += (_, _) => { InsertRequested?.Invoke(symbol.Command); _preferences.Record(symbol.Command); _preferences.Save(); if (category == "Recent") Refresh(); };
            var menu = new ContextMenu(); var favorite = new MenuItem { Header = _preferences.Favorites.Contains(symbol.Command) ? "Remove favorite" : "Add favorite" };
            favorite.Click += (_, _) => { if (!_preferences.Favorites.Remove(symbol.Command)) _preferences.Favorites.Add(symbol.Command); _preferences.Save(); Refresh(); }; menu.Items.Add(favorite); button.ContextMenu = menu;
            _items.Children.Add(button);
        }
        if (_items.Children.Count == 0) _items.Children.Add(new TextBlock { Text = "No matching symbols.", Margin = new Thickness(4) });
    }
}
