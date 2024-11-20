const std = @import("std");
const winmd = @import("winmd");

pub fn oom(e: error{OutOfMemory}) noreturn {
    @panic(@errorName(e));
}

pub fn fatal(comptime fmt: []const u8, args: anytype) noreturn {
    std.log.err(fmt, args);
    std.process.exit(0xff);
}

pub fn main() !void {
    var arena_instance = std.heap.ArenaAllocator.init(std.heap.page_allocator);
    const arena = arena_instance.allocator();

    const all_args = try std.process.argsAlloc(arena);
    const cmd_args = all_args[1..];
    if (cmd_args.len != 2) fatal("expected 2 cmdline arguments but got {}", .{cmd_args.len});

    const winmd_path = cmd_args[0];
    const out_dir_path = cmd_args[1];

    var out_dir = try std.fs.cwd().openDir(out_dir_path, .{});
    defer out_dir.close();

    const winmd_content = blk: {
        var winmd_file = std.fs.cwd().openFile(winmd_path, .{}) catch |err| fatal(
            "failed to open '{s}' with {s}",
            .{ winmd_path, @errorName(err) },
        );
        defer winmd_file.close();
        break :blk try winmd_file.readToEndAlloc(arena, std.math.maxInt(usize));
    };

    var fbs = std.io.fixedBufferStream(winmd_content);
    var opt: winmd.ReadMetadataOptions = undefined;
    winmd.readMetadata(&fbs, &opt) catch |err| switch (err) {
        error.ReadMetadata => {
            std.log.err("{}", .{opt.fmtError()});
            std.process.exit(0xff);
        },
        else => |e| return e,
    };

    const strings: ?[]u8 = blk: {
        const strings_stream = opt.streams.strings orelse break :blk null;
        break :blk winmd_content[opt.metadata_file_offset + strings_stream.offset ..][0..strings_stream.size];
    };

    std.log.info("TypeDef row count is {}", .{opt.row_count.type_def});

    // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    // REMOVE this
    {
        const type_ref_row_size = winmd.calcRowSize(usize, opt.large_columns.type_ref);
        var type_ref_offset: usize = opt.table_data_file_offset + @as(u64, opt.table_offset_type_ref);
        for (0..opt.row_count.type_ref) |type_ref_index| {
            const row = winmd.deserializeRow(
                .type_ref,
                opt.large_columns.type_ref,
                winmd_content[type_ref_offset..][0..winmd.max_row_size.type_ref],
            );
            type_ref_offset += type_ref_row_size;

            const name = winmd.getString(strings, row[1]) orelse fatal("invalid string index {}", .{row[1]});
            const namespace = winmd.getString(strings, row[2]) orelse fatal("invalid string index {}", .{row[2]});
            if (false) std.log.info("TypeRef {}: Namespace '{s}' Name '{s}''", .{ type_ref_index, namespace, name });
        }
    }

    const tables = winmd_content[opt.table_data_file_offset..];

    // first scan all top-level types and sort them by namespace
    // TODO: scan all the types and sort into the api they belong to
    var api_map: std.StringHashMapUnmanaged(Api) = .{};

    {
        const type_def_row_size = winmd.calcRowSize(usize, opt.large_columns.type_def);
        var type_def_offset: usize = opt.table_data_file_offset + @as(u64, opt.table_offset_type_def);
        for (0..opt.row_count.type_def) |type_def| {
            const row = winmd.deserializeRow(
                .type_def,
                opt.large_columns.type_def,
                winmd_content[type_def_offset..][0..winmd.max_row_size.type_def],
            );
            type_def_offset += type_def_row_size;

            // std.log.info(
            //     "TypeDef {}: attr=0x{x} Name={} Namespace={} extends={} fields={} methods={}",
            //     .{
            //         type_def,
            //         row[0],
            //         row[1],
            //         row[2],
            //         row[3],
            //         row[4],
            //         row[5],
            //     },
            // );
            const attributes: u32 = row[0];
            const name = winmd.getString(strings, row[1]) orelse fatal("invalid string index {}", .{row[1]});
            const namespace = winmd.getString(strings, row[2]) orelse fatal("invalid string index {}", .{row[2]});

            if (std.mem.eql(u8, namespace, "")) {
                if (std.mem.eql(u8, name, "<Module>")) continue;

                std.log.info("TODO: handle type '{s}'", .{name});
                continue;

                // // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                // // TODO: handle these types
                // if (std.mem.eql(u8, name, "_u_e__Struct") or
                //     std.mem.eql(u8, name, "_Block_e__Struct") or
                //     std.mem.eql(u8, name, "_Anonymous_e__Struct") or
                //     std.mem.eql(u8, name, "_Anonymous_e__Union") or
                //     std.mem.eql(u8, name, "_Anonymous1_e__Union") or
                //     std.mem.eql(u8, name, "_Anonymous2_e__Union") or
                //     std.mem.eql(u8, name, "_Attribute_e__Union") or
                //     std.mem.eql(u8, name, "_HeaderArm64_e__Struct") or
                //     std.mem.eql(u8, name, "_Values_e__Union") or
                //     std.mem.eql(u8, name, "_Region_e__Struct") or
                //     std.mem.eql(u8, name, "_RequestType_e__Union"))
                // {
                //     continue;
                // }
            }

            const shared_prefix = "Windows.Win32.";
            if (!std.mem.startsWith(u8, namespace, shared_prefix)) {
                std.debug.panic("Unexpected Namespace '{s}'", .{namespace});
            }

            const fields = row[4];
            const methods = row[5];
            if (false) std.log.info("TypeDef {}: Namespace '{s}' Name '{s}' Attr=0x{x}", .{ type_def, namespace, name, attributes });

            const api_name = namespace[shared_prefix.len..];
            const entry = api_map.getOrPut(arena, api_name) catch |e| oom(e);
            if (!entry.found_existing) {
                entry.value_ptr.* = .{};
            }
            const api = entry.value_ptr;

            // The "Apis" type is a specially-named type reserved to contain all the constant
            // and function declarations for an api.
            if (std.mem.eql(u8, name, "Apis")) {
                enforce(
                    api.apis_type_def == null,
                    "multiple 'Apis' types in the same namespace",
                    .{},
                );

                const next_row_fields = blk: {
                    if (fields == 0) break :blk 0;
                    if (type_def < opt.row_count.type_def) {
                        const next_row = winmd.deserializeRow(
                            .type_def,
                            opt.large_columns.type_def,
                            winmd_content[type_def_offset..][0..winmd.max_row_size.type_def],
                        );
                        const next_fields = next_row[4];
                        if (next_fields == 0) @panic("possible(1)?");
                        if (next_fields < fields) std.debug.panic("{} <= {}", .{ next_fields, fields });
                        //std.debug.panic("fields is {}, next_fields is {}", .{ fields, next_fields });
                        break :blk next_fields;
                    }
                    @panic("todo");
                };

                api.apis_type_def = .{
                    .type_def = @intCast(type_def),
                    .fields_start = fields,
                    .fields_limit = next_row_fields,
                    .methods = methods,
                };
                //api.Constants = typeDef.GetFields();
                //api.Funcs = typeDef.GetMethods();
            } else {
                //TypeGenInfo typeInfo = TypeGenInfo.CreateNotNested(mr, typeDef, typeName, typeNamespace, apiNamespaceToName);
                //this.typeMap.Add(typeDefHandle, typeInfo);
                //api.AddTopLevelType(typeInfo);
                try api.type_defs.append(arena, @intCast(type_def));
            }
        }
    }

    {
        var it = api_map.iterator();
        var api_index: usize = 0;
        while (it.next()) |entry| : (api_index += 1) {
            const name = entry.key_ptr.*;
            const api = entry.value_ptr;

            var basename_buf: [200]u8 = undefined;
            const basename = std.fmt.bufPrint(&basename_buf, "{s}.json", .{name}) catch @panic(
                "increase size of basename_buf",
            );

            std.log.info(
                "{}/{}: generating {s} with {} types",
                .{ api_index + 1, api_map.count(), basename, api.type_defs.items.len },
            );
            var file = try out_dir.createFile(basename, .{});
            defer file.close();
            try generateApi(file.writer().any(), &opt, name, api, strings, tables);
        }
    }
}

fn enforce(cond: bool, comptime fmt: []const u8, args: anytype) void {
    if (!cond) std.debug.panic(fmt, args);
}

const Api = struct {
    // The special "Apis" type whose fields are constants and methods are functions
    apis_type_def: ?struct {
        type_def: u32,
        fields_start: u32,
        fields_limit: u32,
        methods: u32,
    } = null,
    type_defs: std.ArrayListUnmanaged(u32) = .{},
};

const constant_filters_by_api = std.StaticStringMap(std.StaticStringMap(void)).initComptime(.{
    .{
        "Devices.Usb",
        std.StaticStringMap(void).initComptime(.{
            // This was marked as "duplicated" in the C# generator...is this still the case?
            .{ "WinUSB_TestGuid", {} },
        }),
    },
    .{
        "Media.MediaFoundation",
        std.StaticStringMap(void).initComptime(.{
            // For some reason the C# generator would get BadImageFormatException: 'Read out of bounds.' when
            // we tried to process these, maybe we can now process them with the zig version?
            .{ "MEDIASUBTYPE_P208", {} },
            .{ "MEDIASUBTYPE_P210", {} },
            .{ "MEDIASUBTYPE_P216", {} },
            .{ "MEDIASUBTYPE_P010", {} },
            .{ "MEDIASUBTYPE_P016", {} },
            .{ "MEDIASUBTYPE_Y210", {} },
            .{ "MEDIASUBTYPE_Y216", {} },
            .{ "MEDIASUBTYPE_P408", {} },
            .{ "MEDIASUBTYPE_P210", {} },
        }),
    },
});

fn generateApi(
    writer: std.io.AnyWriter,
    opt: *const winmd.ReadMetadataOptions,
    api_name: []const u8,
    api: *const Api,
    strings: ?[]const u8,
    tables: []const u8,
) !void {
    try writer.writeAll("{\n");

    const constant_filter = constant_filters_by_api.get(api_name) orelse std.StaticStringMap(void).initComptime(.{});

    var arena_instance = std.heap.ArenaAllocator.init(std.heap.page_allocator);
    defer arena_instance.deinit();
    const arena = arena_instance.allocator();

    var constants_filtered: std.StringHashMapUnmanaged(void) = .{};
    defer constants_filtered.deinit(arena);

    if (api.apis_type_def) |apis| {
        const field_row_size = winmd.calcRowSize(usize, opt.large_columns.field);
        var next_prefix: []const u8 = "\"Constants\":[";
        var next_sep: []const u8 = "";
        for (apis.fields_start..apis.fields_limit) |field_index| {
            const offset = opt.table_offset_field + field_row_size * field_index;
            const row = winmd.deserializeRow(
                .field,
                opt.large_columns.field,
                tables[offset..][0..winmd.max_row_size.field],
            );

            const attributes: winmd.FieldAttributes = @bitCast(@as(u16, @intCast(0xffff & row[0])));
            const has_value_attributes: winmd.FieldAttributes = .{
                .access = .public,
                .static = true,
                .literal = true,
                .has_default = true,
            };
            const no_value_attributes: winmd.FieldAttributes = .{
                .access = .public,
                .static = true,
            };
            const has_value = if (@as(u16, @bitCast(attributes)) == @as(u16, @bitCast(has_value_attributes)))
                true
            else if (@as(u16, @bitCast(attributes)) == @as(u16, @bitCast(no_value_attributes)))
                false
            else
                // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                false;
            //fatal("unexpected constant field definition attributes: {}", .{attributes});

            // const custom_attr_offset = opt.table_offset_custom_attribute + field_row_size * field_index;
            // const row = winmd.deserializeRow(
            //     .field,
            //     opt.large_columns.field,
            //     tables[offset..][0..winmd.max_row_size.field],
            // );

            const name = winmd.getString(strings, row[1]) orelse fatal("invalid string index {}", .{row[1]});
            if (constant_filter.get(name)) |_| {
                std.log.info("filtering constant '{s}' (api {s})", .{ name, api_name });
                constants_filtered.put(arena, name, {}) catch |e| oom(e);
                continue;
            }

            const sig = row[2];
            try writer.print("{s}{s}{{\n", .{ next_prefix, next_sep });
            next_prefix = "}";
            next_sep = ",";
            try writer.print("\t\"Name\":\"{s}\"\n", .{name});
            try writer.print("\t,\"Type\":{}\n", .{sig});
            try writer.print("\t,\"HasValue\":{}\n", .{has_value});
            // try writer.print(
            //     "    {s}\"field {} '{s}' flags=0x{x} sig={}\"\n",
            //     .{ constant_sep, field_index, name, flags, sig },
            // );
        }
        // constants: {
        //     //if (apis.fields == 0) break constants
        //     std.log.info("TODO: loop through fields {}", .{apis.fields});
        // }
        // TODO: loop through apis.fields
        // const field_row_size = winmd.calcRowSize(usize, opt.large_columns.field);
        // var field_offset: usize = opt.table_data_file_offset + @as(u64, opt.table_offset_field);
        // for (0..opt.row_count.type_def) |type_def| {
        //     //if (apis.fields
        //     //_ = apis;
        //     //try writer.writeAll("    \"TODO: generate constants\"\n");
        // }

        try writer.print("{s}],\n", .{next_prefix});
    }

    for (constant_filter.keys()) |key| {
        if (null == constants_filtered.get(key)) {
            std.log.err("constant filter api '{s}' name '{s}' was not applied", .{ api_name, key });
            std.process.exit(0xff);
        }
    }

    try writer.writeAll("\"Types\":[\n");
    try writer.writeAll("    \"TODO: generate types\"\n");
    try writer.writeAll("]\n\n,\"Functions\":[\n");
    try writer.writeAll("    \"TODO: generate functions\"\n");
    try writer.writeAll("]\n\n,\"UnicodeAliases\":[\n");
    try writer.writeAll("    \"TODO: generate unicode aliases\"\n");
    try writer.writeAll("]\n\n}\n");
}
