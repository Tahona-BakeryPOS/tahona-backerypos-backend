using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// Design naming convention: columns, keys and indexes in snake_case. Tables and schemas
/// are named explicitly in each configuration (<c>ToTable("branches", "org")</c>).
/// </summary>
internal static class SnakeCaseNaming
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnake(property.Name));
            foreach (var key in entity.GetKeys())
                key.SetName(ToSnake(key.GetName() ?? key.ToString()!));
            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnake(fk.GetConstraintName() ?? fk.ToString()!));
            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnake(index.GetDatabaseName() ?? index.ToString()!));
        }
    }

    public static string ToSnake(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
