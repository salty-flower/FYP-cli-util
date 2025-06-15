namespace DataCollection.Presentation.Cli.Services;

public interface IResultRenderer<in T>
{
    Task RenderAsync(T result, CancellationToken cancellationToken = default);
}

public interface IErrorRenderer
{
    Task RenderAsync(object error, CancellationToken cancellationToken = default);
}
