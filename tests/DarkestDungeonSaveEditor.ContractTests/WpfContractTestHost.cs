using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    // WPF permits one Application per process. A full run shares this host across UI groups.
    private static Task RunWpfContractsAsync(Func<Task> verify)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                Application? app = null;
                Exception? failure = null;
                try
                {
                    app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!);
                    while (!File.Exists(Path.Combine(directory.FullName, "DarkestDungeonSaveEditor.sln"))) directory = directory.Parent!;
                    app.Resources = LoadContractTheme(directory.FullName);
                    await verify();
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    app?.Shutdown();
                    frame.Continue = false;
                    if (failure is null) completion.SetResult();
                    else completion.SetException(failure);
                }
            }));
            Dispatcher.PushFrame(frame);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return completion.Task;
    }

    private static ResourceDictionary LoadContractTheme(string repositoryRoot)
    {
        var theme = XDocument.Load(Path.Combine(repositoryRoot, "src/DarkestDungeonSaveEditor.App/App.xaml")).Root!;
        var resources = new XElement(theme.Name.Namespace + "ResourceDictionary",
            theme.Attributes().Where(a => a.IsNamespaceDeclaration), theme.Element(theme.Name.Namespace + "Application.Resources")!.Elements());
        resources.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:DarkestDungeonSaveEditor.App;assembly=DarkestDungeonSaveEditor.App");
        return (ResourceDictionary)XamlReader.Parse(resources.ToString(), new ParserContext
        {
            BaseUri = new Uri("pack://application:,,,/DarkestDungeonSaveEditor.App;component/")
        });
    }
}
