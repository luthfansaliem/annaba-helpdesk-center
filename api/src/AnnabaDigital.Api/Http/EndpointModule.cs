namespace AnnabaDigital.Api.Http;

/// <summary>Extension point: each feature slice maps its endpoints through a module (tests register probe modules here).</summary>
public interface IEndpointModule { void Map(IEndpointRouteBuilder app); }
