using System.Net;

// 诊断输出同时写入 exe 目录 diag.txt，方便 IDE 侧直接读取
var diagPath = Path.Combine(AppContext.BaseDirectory, "diag.txt");
File.Delete(diagPath);
void Out(string line)
{
    Console.WriteLine(line);
    File.AppendAllText(diagPath, line + Environment.NewLine);
}

var targets = new (string Name, string Url)[]
{
    ("百度API", "https://fanyi-api.baidu.com/api/trans/vip/translate"),
    ("谷歌gtx", "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=zh-CN&dt=t&q=hello"),
    ("MyMemory", "https://api.mymemory.translated.net/get?q=hello&langpair=en|zh-CN"),
    ("微软翻译", "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0"),
    ("对照supabase", "https://ivkqcwddcwdyhzbczlqe.supabase.co/"),
};

foreach (var mode in new (string Name, bool UseProxy)[] { ("【直连】", false), ("【系统代理】", true) })
{
    Out(mode.Name);
    HttpClient http;
    if (mode.UseProxy)
    {
        var proxy = WebRequest.GetSystemWebProxy();
        Out($"  读取到系统代理: {proxy?.GetProxy(new Uri("https://example.com"))?.ToString() ?? "无"}");
        http = new HttpClient(new SocketsHttpHandler { Proxy = proxy, UseProxy = proxy != null })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }
    else
    {
        http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    using (http)
    {
        foreach (var t in targets)
        {
            try
            {
                using var resp = await http.GetAsync(t.Url);
                Out($"  {t.Name}: HTTP {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                Out($"  {t.Name}: 失败 {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
Out("诊断完成。");
