using Python.Runtime;
using Serilog;

namespace OverlayTranslate.Python;

public class PythonBridge
{
    private readonly PythonRuntime _runtime;

    public PythonBridge(PythonRuntime runtime)
    {
        _runtime = runtime;
    }

    public T? CallFunction<T>(string moduleName, string functionName, params object[] args)
    {
        try
        {
            using (Py.GIL())
            {
                dynamic module = Py.Import(moduleName);
                dynamic result = module.InvokeMethod(functionName, args.Select(a => a.ToPython()).ToArray());
                return result.As<T>();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Python 调用失败: {Module}.{Function}", moduleName, functionName);
            return default;
        }
    }
}
