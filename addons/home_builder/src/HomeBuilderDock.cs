using Godot;

[Tool]
public partial class HomeBuilderDock : Control
{
    [Signal]
    public delegate void ModeChangedEventHandler(string mode);

    private Button _floorButton;
    private Button _wallButton;
    private Button _ceilingButton;
    private Button _doorButton;
    private Button _windowButton;
    private Button _stairsButton;
    private Label _statusLabel;

    public override void _Ready()
    {
        _floorButton   = GetNode<Button>("MainContainer/FloorButton");
        _wallButton    = GetNode<Button>("MainContainer/WallButton");
        _ceilingButton = GetNode<Button>("MainContainer/CeilingButton");
        _doorButton    = GetNode<Button>("MainContainer/DoorButton");
        _windowButton  = GetNode<Button>("MainContainer/WindowButton");
        _stairsButton  = GetNode<Button>("MainContainer/StairsButton");
        _statusLabel   = GetNode<Label>("MainContainer/StatusLabel");

        var buttons = new System.Collections.Generic.Dictionary<Button, string>
        {
            { _floorButton, "floor" },
            { _wallButton, "walls" },
            { _ceilingButton, "ceiling" },
            { _doorButton, "doors" },
            { _windowButton, "windows" },
            { _stairsButton, "stairs" }
        };

        var group = new ButtonGroup();
        foreach (var (button, mode) in buttons)
        {
            button.ButtonGroup = group;
            button.Pressed += () => OnModeSelected(mode);
        }
    }

    private void OnModeSelected(string mode)
    {
        _statusLabel.Text = $"Modo activo: {mode}";
        EmitSignal(SignalName.ModeChanged, mode);
    }
}
