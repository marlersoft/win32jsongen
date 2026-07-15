pub fn main() !void {
    var arena_instance = std.heap.ArenaAllocator.init(std.heap.page_allocator);
    const arena = arena_instance.allocator();

    const all_args = try std.process.argsAlloc(arena);
    const cmd_args = all_args[1..];
    if (cmd_args.len != 5) errExit("expected 5 cmdline arguments but got {}", .{cmd_args.len});

    const version = cmd_args[0];
    const src_path = cmd_args[1];
    const readme_path = cmd_args[2];
    const license_path = cmd_args[3];
    const repo_path = cmd_args[4];

    var repo_dir = try std.fs.cwd().makeOpenPath(repo_path, .{ .iterate = true });
    defer repo_dir.close();

    // Delete every existing entry (except .git) so files removed since the last
    // generation don't linger. We collect the names first because deleting
    // entries while iterating the same directory handle is unsafe.
    {
        var names: std.ArrayListUnmanaged([]const u8) = .{};
        var it = repo_dir.iterate();
        while (try it.next()) |entry| {
            if (std.mem.eql(u8, entry.name, ".git")) continue;
            names.append(arena, try arena.dupe(u8, entry.name)) catch |e| oom(e);
        }
        for (names.items) |name| try repo_dir.deleteTree(name);
    }

    const dst_path = try std.fs.path.join(arena, &.{ repo_path, "api" });
    var dst_dir = try repo_dir.makeOpenPath("api", .{});
    defer dst_dir.close();

    var src_dir = try std.fs.cwd().openDir(src_path, .{ .iterate = true });
    defer src_dir.close();
    var it = src_dir.iterate();
    var file_count: u32 = 0;
    while (try it.next()) |entry| {
        switch (entry.kind) {
            .file => {},
            else => |kind| std.debug.panic(
                "unexpected directory entry kind '{t}' for '{s}' in '{s}'",
                .{ kind, entry.name, src_path },
            ),
        }
        std.debug.assert(std.mem.endsWith(u8, entry.name, ".json"));
        try src_dir.copyFile(entry.name, dst_dir, entry.name, .{});
        file_count += 1;
    }
    std.debug.print("installed {} files to '{s}'\n", .{ file_count, dst_path });

    try repo_dir.writeFile(.{ .sub_path = "version.txt", .data = version });
    std.debug.print("wrote version '{s}' to '{s}/version.txt'\n", .{ version, repo_path });

    try std.fs.cwd().copyFile(readme_path, repo_dir, "README.md", .{});
    try std.fs.cwd().copyFile(license_path, repo_dir, "LICENSE", .{});
    std.debug.print("copied README.md and LICENSE to '{s}'\n", .{repo_path});
}

fn errExit(comptime fmt: []const u8, args: anytype) noreturn {
    std.log.err(fmt, args);
    std.process.exit(0xff);
}

fn oom(e: error{OutOfMemory}) noreturn {
    switch (e) {
        error.OutOfMemory => errExit("out of memory", .{}),
    }
}

const std = @import("std");
