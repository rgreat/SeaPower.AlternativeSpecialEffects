using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Input;
using HarmonyLib;
using Noesis;

namespace AlternativeSpecialEffects
{
    /// <summary>
    /// The entries that open the settings window: a button in the main menu (under Mod Manager) and one in the pause menu
    /// (Esc in a mission or a campaign, under Options). Both are put in when the menu is built, so nothing looks for them later.
    ///  - Main menu: MainMenuView builds its XAML in the constructor; the button copies the look of the others (style MenuButton, size 25).
    ///  - Pause menu: PauseMenuViewModel.Initialise(list) gets the list of PauseMenuButton; the mod's one is added after "Options".
    ///    The game fills a button's text from its language table by (section, key), which has no key for the mod: the button is made
    ///    with an existing key and gets the mod's text, which is set again on every language change (after the game's own handler).
    /// </summary>
    public partial class AsePlugin
    {
        static Type _tPauseButton, _tCommand, _tLangHandler;
        static ConstructorInfo _ctorPauseButton, _ctorCommand;
        static MethodInfo _setContent;
        static FieldInfo _fSection, _fKey;
        static object _langSubscribedTo;
        static readonly List<WeakReference> _pauseButtons = new List<WeakReference>();

        static void HookMenus(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var main = asm.GetType("SeapowerUI.MainMenuView");
            if (main != null)
            {
                var ctor = main.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (ctor != null) harmony.Patch(ctor, postfix: Hm("MainMenuCtorPostfix"));
                else Warn("MainMenuView constructor not found: no settings button in the main menu.");
            }
            else Warn("MainMenuView not found: no settings button in the main menu.");

            _tPauseButton = asm.GetType("SeapowerUI.ViewModels.PauseMenuButton");
            _tCommand = asm.GetType("SeaPower.DelegateCommand");
            _tLangHandler = asm.GetType("SeaPower.LanguageResourceHandler");
            var vm = asm.GetType("SeapowerUI.ViewModels.PauseMenuViewModel");
            if (_tPauseButton != null && _tCommand != null && vm != null && _tLangHandler != null)
            {
                _ctorPauseButton = _tPauseButton.GetConstructor(new[] { typeof(string), typeof(string), typeof(ICommand), typeof(bool), typeof(bool) });
                _ctorCommand = _tCommand.GetConstructor(new[] { typeof(Action<object>) });
                var content = _tPauseButton.GetProperty("ButtonContent", all);
                _setContent = content == null ? null : content.GetSetMethod(true);
                _fSection = _tPauseButton.GetField("contentSection", all);
                _fKey = _tPauseButton.GetField("contentKey", all);
                var init = vm.GetMethod("Initialise", all, null, new[] { typeof(List<>).MakeGenericType(_tPauseButton) }, null);
                if (_ctorPauseButton != null && _ctorCommand != null && _setContent != null && _fSection != null && _fKey != null && init != null)
                    harmony.Patch(init, prefix: Hm("PauseInitialisePrefix"));
                else Warn("Pause menu members not found: no settings button in the pause menu.");
            }
            else Warn("Pause menu types not found: no settings button in the pause menu.");
        }

        // ------------------------------------------------------------ main menu

        static void MainMenuCtorPostfix(object __instance)
        {
            if (!Enabled) return;
            try
            {
                var view = __instance as FrameworkElement;
                var scroll = view == null ? null : view.FindName("scrollViewer") as ScrollViewer;
                var list = scroll == null ? null : scroll.Content as StackPanel;
                if (list == null) { Trace("main menu: the button list was not found"); return; }
                var kids = list.Children;
                int n = kids.Count;
                // ... Options, Mod Manager, (spacer), Credits, (spacer), Exit: the button goes under Mod Manager
                if (n < 5 || !(kids[n - 4] is TextBlock) || !(kids[n - 1] is Button)) { Trace("main menu: unexpected button list (" + n + ")"); return; }
                var b = new Button();
                var style = view.TryFindResource("MenuButton") as Style;
                if (style != null) b.Style = style;
                b.FontSize = 25f;
                b.Content = Loc.T("menu.button").ToUpperInvariant();
                b.Click += (s, e) => OpenSettings(GameView("SeapowerUI.MainMenuView") ?? view);
                kids.Insert(n - 4, b);
            }
            catch (Exception e) { Trace("MainMenuCtorPostfix: " + e); }
        }

        // ------------------------------------------------------------ pause menu

        static void PauseInitialisePrefix(IList pauseMenuButtons)
        {
            if (!Enabled || pauseMenuButtons == null) return;
            try
            {
                int at = -1;
                for (int i = 0; i < pauseMenuButtons.Count; i++)
                {
                    var b = pauseMenuButtons[i];
                    if (b != null && "PauseMenu".Equals(_fSection.GetValue(b)) && "Options".Equals(_fKey.GetValue(b))) { at = i; break; }
                }
                if (at < 0) return;   // the menu without Options (confirmation pages): nothing to add
                Action<object> open = o => OpenSettings(GameView("SeapowerUI.PauseMenuView"));
                var cmd = _ctorCommand.Invoke(new object[] { open });
                // an existing key keeps the game's constructor from logging a missing text; the text is the mod's
                var button = _ctorPauseButton.Invoke(new object[] { "PauseMenu", "Options", cmd, true, true });
                _setContent.Invoke(button, new object[] { Loc.T("menu.button") });
                pauseMenuButtons.Insert(at + 1, button);
                _pauseButtons.Add(new WeakReference(button));
                if (_pauseButtons.Count > 8) _pauseButtons.RemoveAt(0);
                SubscribeLanguage();
            }
            catch (Exception e) { Trace("PauseInitialisePrefix: " + e); }
        }

        /// <summary>
        /// The game's buttons set their text on LanguageChanged; the mod's handler runs after theirs (it is subscribed again after each button is
        /// made, and handlers run in subscription order), so the mod's text stays.
        /// </summary>
        static void SubscribeLanguage()
        {
            var si = _tLangHandler.BaseType;   // Singleton<LanguageResourceHandler>
            var instProp = si == null ? null : si.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var inst = instProp == null ? null : instProp.GetValue(null, null);
            var ev = _tLangHandler.GetEvent("LanguageChanged");
            if (inst == null || ev == null) return;
            System.EventHandler h = LanguageChanged;
            ev.RemoveEventHandler(inst, h);
            ev.AddEventHandler(inst, h);
            _langSubscribedTo = inst;
        }

        static void LanguageChanged(object sender, System.EventArgs e)
        {
            try
            {
                foreach (var w in _pauseButtons)
                {
                    var b = w.Target;
                    if (b != null) _setContent.Invoke(b, new object[] { Loc.T("menu.button") });
                }
            }
            catch (Exception ex) { Trace("LanguageChanged: " + ex.Message); }
        }

        // ------------------------------------------------------------ opening the window

        /// <summary>The game's menu view (a static Instance field of the view class): the window is put on its screen.</summary>
        static UIElement GameView(string typeName)
        {
            var asm = GameAssembly();
            var t = asm == null ? null : asm.GetType(typeName);
            var f = t == null ? null : t.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            return f == null ? null : f.GetValue(null) as UIElement;
        }

        static void OpenSettings(UIElement anchor)
        {
            if (anchor == null) { Warn("Settings: the menu screen was not found."); return; }
            SettingsWindow.Open(anchor, null);
        }
    }
}
