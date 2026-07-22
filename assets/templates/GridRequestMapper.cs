using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Telerik.DataSource;

namespace MyProject.Web.Blazor.Infrastructure;

/// <summary>
/// Telerik gives structured sort descriptors; ANZ's PagedAndSortedResultRequestDto.Sorting is a
/// Dynamic LINQ string ("Name asc"). Every grid needs this translation, so it lives in one place.
/// </summary>
public static class GridRequestMapper
{
    public static string ToAbpSorting(IEnumerable<SortDescriptor> sorts, string fallback)
    {
        var clauses = sorts?
            .Select(s => $"{s.Member} {(s.SortDirection == ListSortDirection.Descending ? "desc" : "asc")}")
            .ToList();

        // Dynamic LINQ throws on an empty string, and an unsorted paged query returns
        // non-deterministic pages — so the fallback is not optional.
        return clauses is { Count: > 0 } ? string.Join(", ", clauses) : fallback;
    }

    /// <summary>
    /// Pulls a single-field contains-filter out of the grid's filter tree onto one DTO property.
    /// Deliberately simple: translating Telerik's full composite filter tree into Dynamic LINQ is a
    /// maintenance trap. If rich filtering is needed, add explicit properties to the input DTO instead.
    /// </summary>
    public static string ExtractSimpleFilter(IEnumerable<IFilterDescriptor> filters, string member)
    {
        return filters?
            .OfType<CompositeFilterDescriptor>()
            .SelectMany(c => c.FilterDescriptors.OfType<FilterDescriptor>())
            .Concat(filters.OfType<FilterDescriptor>())
            .FirstOrDefault(f => f.Member == member)?
            .Value?.ToString();
    }
}
