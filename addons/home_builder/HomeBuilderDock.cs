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

        var group = new ButtonGroup();
        _floorButton.ButtonGroup   = group;
        _wallButton.ButtonGroup    = group;
        _ceilingButton.ButtonGroup = group;
        _doorButton.ButtonGroup    = group;
        _windowButton.ButtonGroup  = group;
        _stairsButton.ButtonGroup  = group;

        _floorButton.Pressed   += () => OnModeSelected("floor");
        _wallButton.Pressed    += () => OnModeSelected("walls");
        _ceilingButton.Pressed += () => OnModeSelected("ceiling");
        _doorButton.Pressed    += () => OnModeSelected("doors");
        _windowButton.Pressed  += () => OnModeSelected("windows");
        _stairsButton.Pressed  += () => OnModeSelected("stairs");
    }

    private void OnModeSelected(string mode)
    {
        _statusLabel.Text = $"Modo activo: {mode}";
        EmitSignal(SignalName.ModeChanged, mode);
    }
}
