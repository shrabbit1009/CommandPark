using System.Runtime.InteropServices;
using System.Text;

namespace CommandPark.Background;

class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxExW")]
    public static extern int MessageBoxExW
    (
        nint hWnd,
        string lpText,
        string lpCaption,
        uint uType,
        ushort wLanguageId
    );   // ← WORD 对应 ushort
    
    
    
    static int Main(string[] args)
    {
        // Console.WriteLine("Start Key Listening.");
        // var path   = @"C:\Users\haora\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\SDR#.lnk";
        // var buffer = new byte[2];
        // var fs     = File.OpenRead(path);
        // fs.ReadExactly(buffer, 0, 2);
        //
        // Console.WriteLine($"文件头是 {Encoding.ASCII.GetString(buffer)}({Convert.ToHexString(buffer)})");
        
        return MessageBoxExW(nint.Zero, "Hello World", "Hello World", 0, 0);
    }
}