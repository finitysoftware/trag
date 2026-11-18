using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class FileDiffTextTests
{
    // A side with no changed line says nothing; a preview says its count covers only the rows shown.
    [Fact]
    public void WhenASideChangedLines_ShouldCountThemAndMarkAPreview()
    {
        Assert.Equal(["", "1 line added", "3 lines removed (preview)"],
            [FileDiffText.Added(0, false), FileDiffText.Added(1, false), FileDiffText.Removed(3, true)]);
    }

    // A file saved with a byte order mark differs from one without it only by a character no row shows: a changed line
    // that starts with the mark says so after its text, and an unchanged one keeps quiet, as it does in every diff of
    // such a file.
    [Fact]
    public void WhenAChangedLineStartsWithAByteOrderMark_ShouldSayItAfterTheText()
    {
        var added = new PatchLine('+', "+﻿using System;");
        var unchanged = new PatchLine(' ', " ﻿using System;");

        Assert.Equal("+﻿using System;  · UTF-8 BOM", FileDiffText.Shown(added, false));
        Assert.Equal(" ﻿using System;", FileDiffText.Shown(unchanged, false));
        Assert.Equal(FileDiffText.Shown(added, false).Length, FileDiffText.ShownColumns(added, false));
    }

    // Git's status letter is the badge's word; one Trag does not name is a modification.
    [Fact]
    public void WhenAChangeHasAStatusLetter_ShouldNameIt()
    {
        Assert.Equal(["New File", "New File", "Deleted File", "Renamed", "Copied", "Conflict", "Modified"],
            "A?DRCUM".Select(FileDiffText.ChangeLabel));
    }
}
