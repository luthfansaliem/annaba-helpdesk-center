using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Api.Http;

/// <summary>Email links always use the configured origin — never the Host header (prevents host-header poisoning of reset links).</summary>
public sealed class AppLinks(IOptions<ApiOptions> options) : IAppLinks { public string Origin => options.Value.Origin; }
