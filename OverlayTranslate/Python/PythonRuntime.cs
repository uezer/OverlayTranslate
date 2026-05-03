using Python.Runtime;
using Serilog;

namespace OverlayTranslate.Python;

public class PythonRuntime : IDisposable
{
    private bool _initialized;

    public void Initialize(string? pythonHome = null)
    {
        if (_initialized) return;

        try
        {
            if (!string.IsNullOrEmpty(pythonHome))
                Runtime.PythonDLL = pythonHome;

            PythonEngine.Initialize();
            _initialized = true;
            Log.Information("Python 运行时初始化成功");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Python 运行时初始化失败");
        }
    }

    public dynamic? Execute(string code)
    {
        if (!_initialized) return null;
        using (Py.GIL())
        {
            return PythonEngine.Eval(code);
        }
    }

    public void Dispose()
    {
        if (_initialized)
        {
            PythonEngine.Shutdown();
            _initialized = false;
        }
    }
}
