namespace CommandPark.Background;

public interface IKeyListener
{
    /// <summary>
    /// "修饰符+按键格式"
    /// </summary>
    public string Key { get; set; }
    
    /// <summary>
    /// 按下事件
    /// </summary>
    public event EventHandler KeyPressed;

    public void StartListening();
    
    public void StopListening();
}