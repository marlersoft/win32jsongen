pub fn main() !void {
    var arena_instance = std.heap.ArenaAllocator.init(std.heap.page_allocator);
    const arena = arena_instance.allocator();

    const all_args = try std.process.argsAlloc(arena);
    const cmd_args = all_args[1..];
    if (cmd_args.len != 2) errExit("expected 2 cmdline arguments but got {}", .{cmd_args.len});

    const src_path = cmd_args[0];
    const dst_path = cmd_args[1];

    try std.fs.cwd().deleteTree(dst_path);
    var dst_dir = try std.fs.cwd().makeOpenPath(dst_path, .{});
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
}

fn errExit(comptime fmt: []const u8, args: anytype) noreturn {
    std.log.err(fmt, args);
    std.process.exit(0xff);
}

const std = @import("std");
