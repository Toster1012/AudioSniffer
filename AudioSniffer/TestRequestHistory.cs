using Microsoft.Extensions.DependencyInjection;

namespace AudioSniffer
{
    public class TestRequestHistory
    {
        public static void Test()
        {
            IServiceProvider service_provider = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider();
        }
    }
}