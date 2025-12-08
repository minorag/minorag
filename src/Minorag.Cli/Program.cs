using Minorag.Cli.Commands;

var root = RootCommandFactory.CreateRootCommand();
return await root.Parse(args).InvokeAsync();
