using RuleScript.LanguageServer;

var server = await RuleScriptLanguageServer.StartAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
await server.WaitForExit;
