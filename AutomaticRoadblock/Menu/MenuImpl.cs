using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows.Forms;
using AutomaticRoadblocks.AbstractionLayer;
using AutomaticRoadblocks.Menu.Switcher;
using AutomaticRoadblocks.Settings;
using Rage;
using RAGENativeUI;
using RAGENativeUI.Elements;

namespace AutomaticRoadblocks.Menu
{
    public class MenuImpl : IMenu
    {
        private readonly ILogger _logger;
        private readonly IGame _game;
        private readonly ISettingsManager _settingsManager;
        private readonly ICollection<IMenuSwitchItem> _menuSwitchItems;

        private static readonly MenuPool MenuPool = new();
        private static readonly List<IMenuComponent<UIMenuItem>> MenuItems = new();

        private UIMenuSwitchMenusItem _menuSwitcher;
        private bool _menuRunning;

        public MenuImpl(ILogger logger, IGame game, ISettingsManager settingsManager, ICollection<IMenuSwitchItem> menuSwitchItems)
        {
            _logger = logger;
            _game = game;
            _settingsManager = settingsManager;
            _menuSwitchItems = menuSwitchItems;
        }

        #region Properties

        /// <inheritdoc />
        public bool IsMenuInitialized { get; private set; }

        /// <inheritdoc />
        public bool IsShown { get; private set; }

        /// <inheritdoc />
        public int TotalItems => MenuItems.Count;

        #endregion

        #region IMenu

        /// <inheritdoc />
        public void RegisterComponent(IMenuComponent<UIMenuItem> component)
        {
            Assert.NotNull(component, "component cannot be null");
            var menuSwitchItem = _menuSwitchItems.First(x => x.Type == component.Type);

            menuSwitchItem.Menu.AddItem(component.MenuItem);
            menuSwitchItem.Menu.RefreshIndex();

            MenuItems.Add(component);
        }

        /// <inheritdoc />
        public void Activate()
        {
            if (IsMenuInitialized)
                return;

            _logger.Trace("Adding MenuImpl.Process to FrameRender handler...");
            StartKeyListener();
            IsMenuInitialized = true;
            _logger.Trace("MenuImpl.Process added to FrameRender handler");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _menuRunning = false;
            IsMenuInitialized = false;
        }

        #endregion

        #region Functions

        [IoC.PostConstruct]
        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        private void Init()
        {
            _logger.Trace("Initializing menu");
            _logger.Debug(
                $"Configured menu key combination: {_settingsManager.GeneralSettings.OpenMenuKey} + {_settingsManager.GeneralSettings.OpenMenuModifierKey}");

            try
            {
                _logger.Trace("Creating sub-menu's");
                _menuSwitchItems
                    .Select(x => x.Menu)
                    .ToList()
                    .ForEach(x => MenuPool.Add(x));

                _logger.Trace("Creating menu switcher");
                CreateMenuSwitcher();

                _logger.Trace("Initializing sub-menu's");
                AddMenuSwitcherToEachMenu();

                Activate();
            }
            catch (Exception ex)
            {
                _logger.Error($"An unexpected error occurred while initializing the menu with error {ex.Message}", ex);
                _game.DisplayPluginNotification("an unexpected error occurred");
            }
        }

        private void OnItemSelect(UIMenu sender, UIMenuItem selectedItem, int index)
        {
            var menuComponent = MenuItems.FirstOrDefault(x => x.MenuItem == selectedItem);

            try
            {
                if (menuComponent == null)
                    throw new MenuException("No menu item action found for the selected menu item", selectedItem);

                menuComponent.OnMenuActivation(this);

                if (menuComponent.IsAutoClosed)
                    CloseMenu();
            }
            catch (MenuException ex)
            {
                _logger.Error(ex.Message, ex);
                _game.DisplayPluginNotification("could not invoke menu item, see log files for more info");
            }
            catch (Exception ex)
            {
                _logger.Error($"An unexpected error occurred while activating the menu item {ex.Message}", ex);
                _game.DisplayPluginNotification("an unexpected error occurred while invoking the menu action");
            }
        }

        private bool IsMenuKeyPressed()
        {
            var generalSettings = _settingsManager.GeneralSettings;
            var secondKey = generalSettings.OpenMenuModifierKey;
            var secondKeyDown = secondKey == Keys.None;

            if (!secondKeyDown && Game.IsKeyDownRightNow(secondKey))
                secondKeyDown = true;

            return Game.IsKeyDown(generalSettings.OpenMenuKey) && secondKeyDown;
        }

        private void CloseMenu()
        {
            _menuSwitcher.CurrentMenu.Visible = false;
        }

        private void AddMenuSwitcherToEachMenu()
        {
            foreach (var menuSwitchItem in _menuSwitchItems)
            {
                menuSwitchItem.Menu.AddItem(_menuSwitcher, 0);
                menuSwitchItem.Menu.RefreshIndex();
                menuSwitchItem.Menu.OnItemSelect += OnItemSelect;
            }
        }

        private void CreateMenuSwitcher()
        {
            var displayItems = _menuSwitchItems
                .Select(x => new DisplayItem(x.Menu, x.DisplayText))
                .ToList();

            _menuSwitcher = new UIMenuSwitchMenusItem("Mode", null, displayItems);
        }

        private void StartKeyListener()
        {
            _menuRunning = true;
            _game.NewSafeFiber(() =>
            {
                while (_menuRunning)
                {
                    _game.FiberYield();
                    MenuPool.ProcessMenus();
                    
                    try
                    {
                        if (IsMenuKeyPressed())
                        {
                            _menuSwitcher.CurrentMenu.Visible = !_menuSwitcher.CurrentMenu.Visible;
                            IsShown = _menuSwitcher.CurrentMenu.Visible;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"An unexpected error occurred while processing the menu with error {ex.Message}", ex);
                        _game.DisplayPluginNotification("an unexpected error occurred");
                    }
                }
                
            }, "MenuImpl.KeyListener");
        }

        #endregion
    }
}