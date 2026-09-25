namespace CommandPark.Background;

public class MockKeyListener : IKeyListener
{
    private bool _isListening;
    
    public string Key { get; set; }
    
    public event EventHandler? KeyPressed;
    
    public void StartListening() => _isListening = true;
    public void StopListening() =>  _isListening = false;

    /// <summary>
    /// 按键。null 为直接触发，设置其他按键将在完全相同时触发。
    /// </summary>
    /// <param name="targetKey"></param>
    public void Pause(string? targetKey = null)
    {
        if (targetKey is null || targetKey == Key)
        {
            KeyPressed?.Invoke(this, EventArgs.Empty);
            return;
        }
    }
}