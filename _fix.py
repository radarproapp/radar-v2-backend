import io, subprocess

P = "Services/LessonResourceService.cs"

prev = subprocess.run(
    ["git", "show", "HEAD~1:Services/LessonResourceService.cs"],
    capture_output=True, text=True, check=True).stdout

# The block my over-eager deletion took with it: everything from the candidate helpers down to
# (but not including) the summariser that was genuinely dead.
start = prev.index("    /// <summary>\n    /// How many of each kind go forward to be judged.")
end = prev.index("    /// <summary>\n    /// Summarises every lesson's videos at once, with a deadline.")
block = prev[start:end]

s = io.open(P, encoding="utf-8").read()

anchor = "    private async Task SummariseVideosAsync("
doc = s.rindex("    /// <summary>", 0, s.index(anchor))

assert "VideoCandidatesAsync" not in s, "already present"
s = s[:doc] + block + s[doc:]

io.open(P, "w", encoding="utf-8", newline="\n").write(s)
print("restored the candidate helpers")
