using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Dock.Avalonia.Internal;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Dock.Avalonia.Controls;

/// <summary>
/// Interaction logic for <see cref="HostWindow"/> xaml.
/// </summary>
[PseudoClasses(":toolwindow", ":dragging")]
public class HostWindow : Window, IHostWindow
{
    private readonly DockManager _dockManager;
    private readonly HostWindowState _hostWindowState;
    private List<Control> _chromeGrips = new();
    private HostWindowTitleBar? _hostWindowTitleBar;
    private bool _mouseDown, _draggingWindow;

    /// <summary>
    /// Define <see cref="IsToolWindow"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsToolWindowProperty = 
        AvaloniaProperty.Register<HostWindow, bool>(nameof(IsToolWindow));

    /// <summary>
    /// Define <see cref="ToolChromeControlsWholeWindow"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ToolChromeControlsWholeWindowProperty =
        AvaloniaProperty.Register<HostWindow, bool>(nameof(ToolChromeControlsWholeWindow));

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(HostWindow);

    /// <summary>
    /// Gets or sets if this is the tool window.
    /// </summary>
    public bool IsToolWindow
    {
        get => GetValue(IsToolWindowProperty);
        set => SetValue(IsToolWindowProperty, value);
    }

    /// <summary>
    /// Gets or sets if the tool chrome controls the whole window.
    /// </summary>
    public bool ToolChromeControlsWholeWindow
    {
        get => GetValue(ToolChromeControlsWholeWindowProperty);
        set => SetValue(ToolChromeControlsWholeWindowProperty, value);
    }

    /// <inheritdoc/>
    public IDockManager DockManager => _dockManager;

    /// <inheritdoc/>
    public IHostWindowState HostWindowState => _hostWindowState;

    /// <inheritdoc/>
    public bool IsTracked { get; set; }

    /// <inheritdoc/>
    public IDockWindow? Window { get; set; }

    /// <summary>
    /// Initializes new instance of the <see cref="HostWindow"/> class.
    /// </summary>
    public HostWindow()
    {
        PositionChanged += HostWindow_PositionChanged;
        LayoutUpdated += HostWindow_LayoutUpdated;

        _dockManager = new DockManager();
        _hostWindowState = new HostWindowState(_dockManager, this);
        UpdatePseudoClasses(IsToolWindow, ToolChromeControlsWholeWindow);
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _hostWindowTitleBar = e.NameScope.Find<HostWindowTitleBar>("PART_TitleBar");
        if (_hostWindowTitleBar is { })
        {
            _hostWindowTitleBar.ApplyTemplate();

            if (_hostWindowTitleBar.BackgroundControl is { })
            {
                _hostWindowTitleBar.BackgroundControl.PointerPressed += (_, args) =>
                {
                    MoveDrag(args);
                };
            }
        }
    }

    private PixelPoint ClientPointToScreenRelativeToWindow(Point clientPoint)
    {
        var absScreenPoint = this.PointToScreen(clientPoint);
        var absScreenWindowPoint = this.PointToScreen(new Point(0, 0));
        var relativeScreenDiff = absScreenPoint - absScreenWindowPoint;
        return relativeScreenDiff;
    }

    private void MoveDrag(PointerPressedEventArgs e)
    {
        if (!ToolChromeControlsWholeWindow)
            return;

        if (Window?.Factory?.OnWindowMoveDragBegin(Window) != true)
        {
            return;
        }

        _mouseDown = true;
        _hostWindowState.Process(ClientPointToScreenRelativeToWindow(e.GetPosition(this)), EventType.Pressed);

        PseudoClasses.Set(":dragging", true);
        _draggingWindow = true;
        BeginMoveDrag(e);
    }

    private void EndDrag(PointerEventArgs e)
    {
        PseudoClasses.Set(":dragging", false);

        Window?.Factory?.OnWindowMoveDragEnd(Window);
        _hostWindowState.Process(ClientPointToScreenRelativeToWindow(e.GetPosition(this)), EventType.Released);
        _mouseDown = false;
        _draggingWindow = false;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_chromeGrips.Any(grip => grip.IsPointerOver))
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                MoveDrag(e);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_draggingWindow)
        {
            EndDrag(e);
        }
    }

    private void HostWindow_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (Window is { } && IsTracked)
        {
            Window.Save();

            if (_mouseDown)
            {
                Window.Factory?.OnWindowMoveDrag(Window);
                _hostWindowState.Process(Position, EventType.Moved);
            }
        }
    }

    private void HostWindow_LayoutUpdated(object? sender, EventArgs e)
    {
        if (Window is { } && IsTracked)
        {
            Window.Save();
        }
    }

    /// <summary>
    /// Attaches grip to chrome.
    /// </summary>
    /// <param name="chromeControl">The chrome control.</param>
    public void AttachGrip(ToolChromeControl chromeControl)
    {
        if (chromeControl.CloseButton is not null)
        {
            chromeControl.CloseButton.Click += ChromeCloseClick;
        }

        if (chromeControl.Grip is { } grip)
        {
            _chromeGrips.Add(grip);
        }

        ((IPseudoClasses)chromeControl.Classes).Add(":floating");
        IsToolWindow = true;
    }

    /// <summary>
    /// Detaches grip to chrome.
    /// </summary>
    /// <param name="chromeControl">The chrome control.</param>
    public void DetachGrip(ToolChromeControl chromeControl)
    {
        if (chromeControl.Grip is { } grip)
        {
            _chromeGrips.Remove(grip);
        }

        if (chromeControl.CloseButton is not null)
        {
            chromeControl.CloseButton.Click -= ChromeCloseClick;
        }
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsToolWindowProperty)
        {
            UpdatePseudoClasses(change.GetNewValue<bool>(), ToolChromeControlsWholeWindow);
        }
        else if (change.Property == ToolChromeControlsWholeWindowProperty)
        {
            UpdatePseudoClasses(IsToolWindow, change.GetNewValue<bool>());
        }
    }

    private void UpdatePseudoClasses(bool isToolWindow, bool toolChromeControlsWholeWindow)
    {
        PseudoClasses.Set(":toolwindow", isToolWindow);
        PseudoClasses.Set(":toolchromecontrolswindow", toolChromeControlsWholeWindow);
    }

    private int CountVisibleToolsAndDocuments(IDockable? dockable)
    {
        switch (dockable)
        {
            case ITool: return 1;
            case IDocument: return 1;
            case IDock dock:
                return dock.VisibleDockables?.Sum(CountVisibleToolsAndDocuments) ?? 0;
            default: return 0;
        }
    }

    private void ChromeCloseClick(object? sender, RoutedEventArgs e)
    {
        if (CountVisibleToolsAndDocuments(DataContext as IRootDock) <= 1)
            Exit();
    }

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Window?.Factory?.HostWindows.Add(this);
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        Window?.Factory?.HostWindows.Remove(this);

        if (Window is { })
        {
            Window.Factory?.OnWindowClosed(Window);

            if (IsTracked)
            {
                Window?.Factory?.RemoveWindow(Window);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (Window is { })
        {
            if (Window.Factory?.OnWindowClosing(Window) == false)
            {
                e.Cancel = true;
                return;
            }
        }

        if (Window is { } && IsTracked)
        {
            Window.Save();

            if (Window.Layout is IDock root)
            {
                if (root.Close.CanExecute(null))
                {
                    root.Close.Execute(null);
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Present(bool isDialog)
    {
        if (isDialog)
        {
            if (!IsVisible)
            {
                if (Window is { })
                {
                    Window.Factory?.OnWindowOpened(Window);
                }

                ShowDialog(null!); // FIXME: Set correct parent window.
            }
        }
        else
        {
            if (!IsVisible)
            {
                if (Window is { })
                {
                    Window.Factory?.OnWindowOpened(Window);
                }

                var ownerDockControl = Window?.Layout?.Factory?.DockControls.FirstOrDefault();
                if (ownerDockControl is Control control && control.GetVisualRoot() is Window parentWindow)
                {
                    Title = parentWindow.Title;
                    Icon = parentWindow.Icon;
                    Show(parentWindow);
                }
                else
                {
                    Show();
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Exit()
    {
        if (Window is { })
        {
            if (Window.OnClose())
            {
                Close();
            }
        }
        else
        {
            Close();
        }
    }

    /// <inheritdoc/>
    public void SetPosition(double x, double y)
    {
        if (!double.IsNaN(x) && !double.IsNaN(y))
        {
            Position = new PixelPoint((int)x, (int)y);
        }
    }

    /// <inheritdoc/>
    public void GetPosition(out double x, out double y)
    {
        x = Position.X;
        y = Position.Y;
    }

    /// <inheritdoc/>
    public void SetSize(double width, double height)
    {
        if (!double.IsNaN(width))
        {
            Width = width;
        }

        if (!double.IsNaN(height))
        {
            Height = height;
        }
    }

    /// <inheritdoc/>
    public void GetSize(out double width, out double height)
    {
        width = Width;
        height = Height;
    }

    /// <inheritdoc/>
    public void SetTitle(string title)
    {
        Title = title;
    }

    /// <inheritdoc/>
    public void SetLayout(IDock layout)
    {
        DataContext = layout;
    }
}
