using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scout.Infrastructure.Dados;

namespace Scout.Infrastructure.Acesso;

/// <summary>Cadastro e conferência de senha (hash PBKDF2 do ASP.NET Identity; a senha nunca é guardada).</summary>
public class Usuarios(ScoutDbContext db)
{
    static readonly PasswordHasher<Usuario> hasher = new();
    public const int TamanhoMinimoSenha = 10;

    public async Task<Usuario> Criar(string email, string nome, string senha, CancellationToken ct)
    {
        email = Normalizar(email);
        if (!email.Contains('@')) throw new ArgumentException("E-mail inválido");
        if (senha.Length < TamanhoMinimoSenha) throw new ArgumentException($"A senha precisa ter pelo menos {TamanhoMinimoSenha} caracteres");

        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct)
            ?? db.Usuarios.Add(new Usuario { Email = email, CriadoEm = DateTime.UtcNow }).Entity;
        usuario.Nome = nome.Trim();
        usuario.SenhaHash = hasher.HashPassword(usuario, senha);
        await db.SaveChangesAsync(ct);
        return usuario;
    }

    /// <summary>Devolve o usuário se a senha confere; null caso contrário (sem dizer qual dos dois errou).</summary>
    public async Task<Usuario?> Autenticar(string email, string senha, CancellationToken ct)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == Normalizar(email), ct);
        if (usuario is null)
        {
            hasher.HashPassword(new Usuario(), senha); // mesmo custo de tempo de um e-mail existente
            return null;
        }
        var resultado = hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senha);
        if (resultado == PasswordVerificationResult.Failed) return null;
        if (resultado == PasswordVerificationResult.SuccessRehashNeeded) usuario.SenhaHash = hasher.HashPassword(usuario, senha);
        usuario.UltimoAcesso = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return usuario;
    }

    public async Task<bool> Remover(string email, CancellationToken ct) =>
        await db.Usuarios.Where(u => u.Email == Normalizar(email)).ExecuteDeleteAsync(ct) > 0;

    static string Normalizar(string email) => email.Trim().ToLowerInvariant();
}
