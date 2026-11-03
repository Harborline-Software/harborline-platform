using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Localization;
using Harborline.UIAdapters.Blazor.Shell;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract (interface revision 3, G6): in a supplied UserMenuLabels record each null or blank field comes from the
// catalog (host, then default) and every other field is kept exactly. The unsupplied default record (the English
// instance itself) takes everything from the catalog, and a fully specified record, including a copy of English,
// renders as before.
public sealed class UserMenuPartialLabelsTests : BunitContext
{
    private static readonly Dictionary<string, string> French = new() { ["navigation.userMenu.trigger"] = "Menu du compte de {name}", ["navigation.userMenu.panel"] = "Menu du compte", ["navigation.userMenu.signOut"] = "Se déconnecter" };
    private static readonly Dictionary<string, string> Arabic = new() { ["navigation.userMenu.trigger"] = "قائمة حساب {name}", ["navigation.userMenu.panel"] = "قائمة الحساب", ["navigation.userMenu.signOut"] = "تسجيل الخروج" };

    public UserMenuPartialLabelsTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<HarborlineLocaleProvider> Localized(string locale, IReadOnlyDictionary<string, string> catalog, UserMenuLabels? labels) =>
        Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, locale).Add(x => x.Catalog, catalog).AddChildContent<HarborlineUserMenu>(c =>
        {
            c.Add(x => x.Identity, new UserIdentity("Avery")).Add(x => x.OnSignOut, () => { });
            if (labels is not null) c.Add(x => x.Labels, labels);
        }));
    // The trigger, panel and sign-out names, read with the surface open, then closed again. Sign-out text is read untrimmed.
    private static (string? Trigger, string? Panel, string? SignOut) Names<T>(IRenderedComponent<T> cut) where T : IComponent
    {
        cut.Find(".hl-user-menu__trigger").Click();
        var names = (cut.Find(".hl-user-menu__trigger").GetAttribute("aria-label"), cut.Find(".hl-user-menu__surface").GetAttribute("aria-label"), cut.Find(".hl-user-menu__sign-out").TextContent);
        cut.Find(".hl-user-menu__trigger").Click();
        return names;
    }

    [Fact]
    public void APartialRecordKeepsItsFieldAndTakesTheRestFromTheArabicCatalog()
    {
        Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "Leave"), Names(Localized("ar-SA", Arabic, new UserMenuLabels(SignOut: "Leave"))));
        Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "Leave"), Names(Localized("ar-SA", Arabic, new UserMenuLabels { SignOut = "Leave" })));
        Assert.Equal(("Who is Avery?", "قائمة الحساب", "تسجيل الخروج"), Names(Localized("ar-SA", Arabic, new UserMenuLabels(Trigger: name => $"Who is {name}?"))));
    }

    [Fact]
    public void AnOmittedOrBlankFieldFallsBackToTheCatalog()
    {
        // Panel omitted (null), SignOut blank: both come from the catalog; the trigger is kept.
        Assert.Equal(("Account of Avery", "قائمة الحساب", "تسجيل الخروج"), Names(Localized("ar-SA", Arabic, new UserMenuLabels(Trigger: name => $"Account of {name}", SignOut: " "))));
        // A trigger that returns blank text, and an empty panel, also fall back.
        Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "Leave"), Names(Localized("ar-SA", Arabic, new UserMenuLabels(Trigger: _ => "\t", Panel: string.Empty, SignOut: "Leave"))));
        // An all-null record is caller text with nothing in it: everything comes from the catalog.
        Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "تسجيل الخروج"), Names(Localized("ar-SA", Arabic, new UserMenuLabels())));
    }

    // The 25 characters of ECMA-262 WhiteSpace and LineTerminator, the set String.prototype.trim strips (T-1107).
    private const string EcmaBlank = "\t\n\v\f\r \u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF";

    [Fact]
    public void BlankMeansOnlyTheEcmaScriptWhiteSpaceAndLineTerminatorSet()
    {
        Assert.Equal(25, EcmaBlank.Length);
        // Each character alone, and all of them together, is blank: the field falls back.
        foreach (var blank in EcmaBlank.Select(character => character.ToString()).Append(EcmaBlank))
            Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "تسجيل الخروج"), Names(Localized("ar-SA", Arabic, new UserMenuLabels(Trigger: _ => blank, Panel: blank, SignOut: blank))));
        // U+FEFF (ZWNBSP) is blank although .NET's char.IsWhiteSpace says it is not; U+0085 (NEL) is not blank
        // although char.IsWhiteSpace says it is; U+200B (ZWSP) is in neither set. Both lanes agree.
        Assert.Equal(("قائمة حساب Avery", "\u0085", "\u200B"), Names(Localized("ar-SA", Arabic, new UserMenuLabels(Trigger: _ => "\uFEFF", Panel: "\u0085", SignOut: "\u200B"))));
    }

    [Fact]
    public void ALocaleChangeUpdatesTheFallbackFieldsAndKeepsTheExplicitOne()
    {
        var labels = new UserMenuLabels(SignOut: "Leave");
        var cut = Localized("fr-FR", French, labels);
        Assert.Equal(("Menu du compte de Avery", "Menu du compte", "Leave"), Names(cut));
        cut.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Catalog, Arabic));
        Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "Leave"), Names(cut));
    }

    [Fact]
    public void AFullySpecifiedEnglishRecordStaysEnglishUnderAnArabicCatalog()
    {
        // Every field supplied, even where it equals the English default: kept exactly.
        Assert.Equal(("Account menu for Avery", "Account menu", "Sign out"), Names(Localized("ar-SA", Arabic, new UserMenuLabels(name => $"Account menu for {name}", "Account menu", "Sign out"))));
        // A copy of English is a supplied record, not the default: only the English instance itself means "unsupplied".
        Assert.Equal(("Account menu for Avery", "Account menu", "Sign out"), Names(Localized("ar-SA", Arabic, UserMenuLabels.English with { })));
        Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "تسجيل الخروج"), Names(Localized("ar-SA", Arabic, UserMenuLabels.English)));
        Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "تسجيل الخروج"), Names(Localized("ar-SA", Arabic, null)));
    }

    [Fact]
    public void ExistingAndNewConstructionShapesCompileAndEnglishIsUnchanged()
    {
        // Existing shapes: positional full construction, English, and with-copies of it.
        UserMenuLabels positional = new(name => $"Menu for {name}", "Menu", "Leave");
        UserMenuLabels copy = UserMenuLabels.English with { };
        UserMenuLabels edited = UserMenuLabels.English with { SignOut = "Leave" };
        // New shapes: named arguments and an object initializer, every other field null.
        UserMenuLabels named = new(SignOut: "Leave");
        UserMenuLabels initialized = new() { SignOut = "Leave" };
        Assert.Equal("Menu for Ada", positional.Trigger!("Ada"));
        Assert.Equal(("Account menu", "Sign out"), (copy.Panel, copy.SignOut));
        Assert.Equal(("Account menu", "Leave"), (edited.Panel, edited.SignOut));
        Assert.Equal((null, null, "Leave"), ((object?)named.Trigger, named.Panel, named.SignOut));
        Assert.Equal(named, initialized);
        // English is the parameter default, the sentinel for "unsupplied". A copy is value-equal to it (with copies the
        // same Trigger delegate) but a different instance, which is why the sentinel test is identity, not equality.
        Assert.Same(UserMenuLabels.English, new HarborlineUserMenu().Labels);
        Assert.Equal(UserMenuLabels.English, copy);
        Assert.NotSame(UserMenuLabels.English, copy);
        Assert.Equal(("Account menu for Ada", "Account menu", "Sign out"), (UserMenuLabels.English.Trigger!("Ada"), UserMenuLabels.English.Panel, UserMenuLabels.English.SignOut));
        // Readers of the properties now see string? (a source-level change only; the runtime type is System.String).
        string? panel = named.Panel;
        Assert.Null(panel);
        Assert.Equal(typeof(string), typeof(UserMenuLabels).GetProperty(nameof(UserMenuLabels.Panel))!.PropertyType);
    }
}
