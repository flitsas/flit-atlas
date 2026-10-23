using System.Data.Common;
using Flit.Atlas.Identidad.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flit.Atlas.Identidad.Tokens;

/// <summary>
/// Deja <c>identidad_oidc_aplicacion</c> y <c>identidad_oidc_scope</c> iguales a la configuración y al registro de
/// scopes al arrancar (ATLAS-ADR-0024). Es idempotente y la configuración manda: crea, actualiza y borra (borrar un
/// cliente revoca sus tokens). Varias réplicas pueden arrancar a la vez: un candado de PostgreSQL las pone en fila.
/// Si la base aún no responde, el arranque sigue y se reintenta en segundo plano.
/// </summary>
internal sealed partial class SincronizadorClientes(
    IServiceScopeFactory alcances,
    IOptions<OpcionesTokens> opciones,
    RegistroScopes scopes,
    ILogger<SincronizadorClientes> log) : IHostedService, IDisposable
{
    /// <summary>Llave del candado de sesión de PostgreSQL (<c>pg_advisory_xact_lock</c>) de esta sincronización.</summary>
    private const long Candado = 0x41544C4153_0001;

    private readonly CancellationTokenSource _detener = new();
    private Task? _reintentos;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SincronizarAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbException or TimeoutException or InvalidOperationException { InnerException: DbException })
        {
            NoSeSincronizo(log, ex);
            _reintentos = ReintentarAsync(_detener.Token);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _detener.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Al cerrar, el host a veces desecha el servicio antes de detenerlo (se vio con WebApplicationFactory en el
            // CI): ya no hay reintentos que detener.
            return;
        }
        if (_reintentos is not null)
        {
            await _reintentos.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public void Dispose()
    {
        // Si el host desecha sin detener antes, los reintentos en segundo plano igual terminan.
        _detener.Cancel();
        _detener.Dispose();
    }

    private async Task ReintentarAsync(CancellationToken ct)
    {
        var espera = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(espera, ct);
                await SincronizarAsync(ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is DbException or TimeoutException or InvalidOperationException { InnerException: DbException })
            {
                NoSeSincronizo(log, ex);
                espera = TimeSpan.FromSeconds(Math.Min(espera.TotalSeconds * 2, 60));
            }
        }
    }

    internal async Task SincronizarAsync(CancellationToken ct)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var aplicaciones = alcance.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var gestorScopes = alcance.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await contexto.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({Candado})", ct);

        foreach (var scope in scopes.Todos)
        {
            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = scope.Nombre,
                DisplayName = scope.Descripcion,
                Description = $"Módulo {scope.Modulo}.",
            };
            descriptor.Resources.Add(OpcionesTokens.AudienciaApi);
            var existente = await gestorScopes.FindByNameAsync(scope.Nombre, ct);
            if (existente is null)
            {
                await gestorScopes.CreateAsync(descriptor, ct);
            }
            else
            {
                await gestorScopes.UpdateAsync(existente, descriptor, ct);
            }
        }
        var scopesSobrantes = new List<object>();
        await foreach (var existente in gestorScopes.ListAsync(count: null, offset: null, ct))
        {
            if (!scopes.Existe(await gestorScopes.GetNameAsync(existente, ct) ?? string.Empty))
            {
                scopesSobrantes.Add(existente);
            }
        }
        foreach (var sobrante in scopesSobrantes)
        {
            await gestorScopes.DeleteAsync(sobrante, ct);
        }

        var configurados = opciones.Value.Clientes;
        foreach (var (id, cliente) in configurados)
        {
            var existente = await aplicaciones.FindByClientIdAsync(id, ct);
            var descriptor = new OpenIddictApplicationDescriptor();
            if (existente is not null)
            {
                // Parte del registro actual para conservar el secreto ya cifrado si no cambió.
                await aplicaciones.PopulateAsync(descriptor, existente, ct);
            }
            Describir(descriptor, id, cliente);
            if (cliente.Tipo == TipoCliente.Confidencial
                && (existente is null || !await aplicaciones.ValidateClientSecretAsync(existente, cliente.Secreto!, ct)))
            {
                // OpenIddict guarda solo un derivado del secreto (PBKDF2), nunca el valor.
                descriptor.ClientSecret = cliente.Secreto;
            }
            if (existente is null)
            {
                await aplicaciones.CreateAsync(descriptor, ct);
                ClienteCreado(log, id);
            }
            else
            {
                await aplicaciones.UpdateAsync(existente, descriptor, ct);
            }
        }

        var clientesSobrantes = new List<object>();
        await foreach (var existente in aplicaciones.ListAsync(count: null, offset: null, ct))
        {
            if (!configurados.ContainsKey(await aplicaciones.GetClientIdAsync(existente, ct) ?? string.Empty))
            {
                clientesSobrantes.Add(existente);
            }
        }
        foreach (var sobrante in clientesSobrantes)
        {
            var id = await aplicaciones.GetClientIdAsync(sobrante, ct);
            // OpenIddict borra con él sus autorizaciones y tokens: el cliente queda revocado.
            await aplicaciones.DeleteAsync(sobrante, ct);
            ClienteBorrado(log, id ?? string.Empty);
        }

        await transaccion.CommitAsync(ct);
    }

    internal static void Describir(OpenIddictApplicationDescriptor descriptor, string id, ClienteTokens cliente)
    {
        descriptor.ClientId = id;
        descriptor.DisplayName = cliente.Nombre ?? id;
        descriptor.ClientType = cliente.Tipo == TipoCliente.Publico ? ClientTypes.Public : ClientTypes.Confidential;
        // Los públicos son apps nativas (CLI, MCP): OpenIddict acepta su redirect de loopback en cualquier puerto.
        descriptor.ApplicationType = cliente.Tipo == TipoCliente.Publico ? ApplicationTypes.Native : ApplicationTypes.Web;
        // Clientes propios de FLIT: no hay pantalla de consentimiento (ATLAS-ADR-0004 §7; los de terceros, si llegan, sí).
        descriptor.ConsentType = ConsentTypes.Implicit;
        if (cliente.Tipo == TipoCliente.Publico)
        {
            descriptor.ClientSecret = null;
        }

        descriptor.RedirectUris.Clear();
        foreach (var uri in cliente.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }
        descriptor.PostLogoutRedirectUris.Clear();

        descriptor.Permissions.Clear();
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
        if (cliente.Tipo == TipoCliente.Confidencial)
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Introspection);
        }
        if (cliente.Flujos.Contains(ClienteTokens.FlujoCodigo))
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        }
        if (cliente.Flujos.Contains(ClienteTokens.FlujoRefresh))
        {
            descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        }
        if (cliente.Flujos.Contains(ClienteTokens.FlujoServicio))
        {
            descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        }
        foreach (var scope in cliente.Scopes.Where(s => s is not Scopes.OpenId))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        descriptor.Requirements.Clear();
        if (cliente.Flujos.Contains(ClienteTokens.FlujoCodigo))
        {
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudieron sincronizar los clientes de OpenIddict; se reintenta en segundo plano")]
    private static partial void NoSeSincronizo(ILogger log, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cliente de OpenIddict creado: {ClienteId}")]
    private static partial void ClienteCreado(ILogger log, string clienteId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cliente de OpenIddict borrado porque ya no está en la configuración: {ClienteId}")]
    private static partial void ClienteBorrado(ILogger log, string clienteId);
}
