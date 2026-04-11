using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("JobCategory")]
public class JobCategoryDbModel
{
    public required string CategoryName { get; set; }
    [ExplicitKey]
    public required string JobName { get; set; }
}