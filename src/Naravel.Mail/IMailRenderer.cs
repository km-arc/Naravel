namespace Naravel.Mail;

/// <summary>
/// A seam the application can implement to turn a template and model into a body string. Naravel declares it but ships no
/// implementation, so no view engine is mandated (PDR-013 / PDR-013a).
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>view()</c> inside a mailable. <b>Why it exists:</b> a shared name for the optional renderer so
/// mailables and docs agree. <b>Usage:</b> <see cref="Mailable.Build"/> is synchronous and pure, so render first
/// (<c>var html = await renderer.RenderAsync(...)</c>) and pass the string into the mailable's constructor.
/// </remarks>
public interface IMailRenderer
{
    /// <summary>Renders a template with a model.</summary>
    /// <param name="template">The application-defined template name.</param>
    /// <param name="model">The model for the template.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    Task<string> RenderAsync(string template, object model, CancellationToken cancellationToken = default);
}
