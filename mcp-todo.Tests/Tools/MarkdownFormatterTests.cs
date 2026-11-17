using System.Collections;
using mcp_todo.Models;
using mcp_todo.Tools.Formatters;
using Xunit;

namespace mcp_todo.Tests.Tools;

public class MarkdownFormatterTests
{
    #region FormatTodoList Tests

    [Fact]
    public void FormatTodoList_WithEmptyList_ReturnsNoTodosMessage()
    {
        // Arrange
        var emptyList = new List<Todo>();

        // Act
        var result = MarkdownFormatter.FormatTodoList(emptyList);

        // Assert
        Assert.Equal("Bu projede listelenecek görev bulunamadı.", result);
    }

    [Fact]
    public void FormatTodoList_WithPopulatedList_GeneratesMarkdownTable()
    {
        // Arrange
        var todos = new List<Todo>
        {
            new()
            {
                Id = 1,
                Title = "Görev 1",
                IsCompleted = false,
                DueDate = new DateTime(2026, 12, 31)
            },
            new()
            {
                Id = 2,
                Title = "Görev 2",
                IsCompleted = true,
                DueDate = null
            }
        };

        // Act
        var result = MarkdownFormatter.FormatTodoList(todos);

        // Assert
        Assert.Contains("| ID | Durum | Görev | Bitiş Tarihi |", result);
        Assert.Contains("|---|---|---|---|", result);
        Assert.Contains("| 1 | [ ] Açık | Görev 1 | 2026-12-31 |", result);
        Assert.Contains("| 2 | [x] Tamamlandı | Görev 2 | - |", result);
    }

    [Fact]
    public void FormatTodoList_WithPipeInTitle_EscapesPipeCharacter()
    {
        // Arrange
        var todos = new List<Todo>
        {
            new()
            {
                Id = 42,
                Title = "Test | Görev | Detay",
                IsCompleted = false,
                DueDate = null
            }
        };

        // Act
        var result = MarkdownFormatter.FormatTodoList(todos);

        // Assert
        Assert.Contains("| 42 | [ ] Açık | Test \\| Görev \\| Detay | - |", result);
    }

    [Fact]
    public void FormatTodoList_WithProjectTitleWithoutHash_PrefixesWithTripleHash()
    {
        // Arrange
        var todos = new List<Todo>
        {
            new() { Id = 1, Title = "Görev", IsCompleted = false }
        };

        // Act
        var result = MarkdownFormatter.FormatTodoList(todos, "Proje Adı");

        // Assert
        Assert.StartsWith("### Proje Adı", result);
        Assert.Contains("| ID | Durum | Görev | Bitiş Tarihi |", result);
    }

    [Fact]
    public void FormatTodoList_WithProjectTitleWithHash_KeepsOriginalHeading()
    {
        // Arrange
        var todos = new List<Todo>
        {
            new() { Id = 1, Title = "Görev", IsCompleted = false }
        };

        // Act
        var result = MarkdownFormatter.FormatTodoList(todos, "# Ana Başlık");

        // Assert
        Assert.StartsWith("# Ana Başlık", result);
        Assert.DoesNotContain("### # Ana Başlık", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatTodoList_WithNullOrWhiteSpaceProjectTitle_DoesNotIncludeTitleHeader(string? projectTitle)
    {
        // Arrange
        var todos = new List<Todo>
        {
            new() { Id = 1, Title = "Görev", IsCompleted = false }
        };

        // Act
        var result = MarkdownFormatter.FormatTodoList(todos, projectTitle);

        // Assert
        Assert.StartsWith("| ID | Durum | Görev | Bitiş Tarihi |", result);
    }

    #endregion

    #region FormatGroupedTodoList Tests

    [Fact]
    public void FormatGroupedTodoList_WithEmptyGroups_ReturnsNoRegisteredTasksMessage()
    {
        // Arrange
        var emptyGrouping = new List<IGrouping<Project, Todo>>();

        // Act
        var result = MarkdownFormatter.FormatGroupedTodoList(emptyGrouping);

        // Assert
        Assert.Equal("Henüz kayıtlı bir göreviniz bulunmuyor.", result);
    }

    [Fact]
    public void FormatGroupedTodoList_WithEmptyProjectInGroup_ReturnsProjectHeaderAndNoTasksMessage()
    {
        // Arrange
        var project = new Project { Id = 10, Name = "Boş Proje" };
        var group = new ProjectTodoGrouping(project, new List<Todo>());
        var groups = new List<IGrouping<Project, Todo>> { group };

        // Act
        var result = MarkdownFormatter.FormatGroupedTodoList(groups);

        // Assert
        Assert.Contains("### Proje: Boş Proje (ID: 10)", result);
        Assert.Contains("Bu projede listelenecek görev bulunamadı.", result);
    }

    [Fact]
    public void FormatGroupedTodoList_WithPopulatedGroups_RendersAllProjectsAndTables()
    {
        // Arrange
        var project1 = new Project { Id = 1, Name = "Proje Alfa" };
        var todo1 = new Todo { Id = 101, Title = "Alfa Görevi", IsCompleted = false };
        var group1 = new ProjectTodoGrouping(project1, new List<Todo> { todo1 });

        var project2 = new Project { Id = 2, Name = "Proje Beta" };
        var todo2 = new Todo { Id = 201, Title = "Beta Görevi", IsCompleted = true, DueDate = new DateTime(2026, 11, 20) };
        var group2 = new ProjectTodoGrouping(project2, new List<Todo> { todo2 });

        var groups = new List<IGrouping<Project, Todo>> { group1, group2 };

        // Act
        var result = MarkdownFormatter.FormatGroupedTodoList(groups);

        // Assert
        Assert.Contains("### Proje: Proje Alfa (ID: 1)", result);
        Assert.Contains("| 101 | [ ] Açık | Alfa Görevi | - |", result);

        Assert.Contains("### Proje: Proje Beta (ID: 2)", result);
        Assert.Contains("| 201 | [x] Tamamlandı | Beta Görevi | 2026-11-20 |", result);
    }

    #endregion

    #region ProjectTodoGrouping Tests

    [Fact]
    public void ProjectTodoGrouping_WithNullProject_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new ProjectTodoGrouping(null!, new List<Todo>()));
        Assert.Equal("project", exception.ParamName);
    }

    [Fact]
    public void ProjectTodoGrouping_WithNullTodos_InitializesEmptyList()
    {
        // Arrange
        var project = new Project { Id = 5, Name = "Test Project" };

        // Act
        var grouping = new ProjectTodoGrouping(project, null!);

        // Assert
        Assert.Equal(project, grouping.Key);
        Assert.Empty(grouping);
    }

    [Fact]
    public void ProjectTodoGrouping_Enumeration_YieldsCorrectTodos()
    {
        // Arrange
        var project = new Project { Id = 5, Name = "Test Project" };
        var todos = new List<Todo>
        {
            new() { Id = 1, Title = "T1" },
            new() { Id = 2, Title = "T2" }
        };

        // Act
        var grouping = new ProjectTodoGrouping(project, todos);

        // Assert - Generic Enumerator
        var enumerated = grouping.ToList();
        Assert.Equal(2, enumerated.Count);
        Assert.Equal("T1", enumerated[0].Title);
        Assert.Equal("T2", enumerated[1].Title);

        // Assert - Non-generic IEnumerable.GetEnumerator()
        var nonGeneric = (IEnumerable)grouping;
        var count = 0;
        foreach (var item in nonGeneric)
        {
            Assert.IsType<Todo>(item);
            count++;
        }
        Assert.Equal(2, count);
    }

    #endregion
}
