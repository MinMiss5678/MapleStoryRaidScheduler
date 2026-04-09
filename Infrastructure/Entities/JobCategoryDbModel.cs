using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("JobCategory")]
public class JobCategoryDbModel
{
    public string CategoryName { get; set; }
    [ExplicitKey]
    public string JobName { get; set; }
}