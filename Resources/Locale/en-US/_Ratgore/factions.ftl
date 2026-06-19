# Guild Selection UI
rat-faction-selector-none = No Guild
rat-faction-selector-no-factions = No guilds created or loaded.

Use the command:
factioncreate <name> <whitelist:true/false> <description>

to create guilds.

A guild is a player-created organization within a faction
rat-faction-selector-select = Select a guild from the list
rat-faction-selector-no-subfaction = No guild
rat-faction-selector-whitelist-required = Whitelist Required

[color=yellow]Whitelist required[/color]
rat-faction-selector-invalid-index = Invalid guild index: { $idx } (total: { $total })
rat-faction-selector-reset = Guild reset.
rat-faction-selector-selected = Selected guild: { $factionName }

# Guild Commands
rat-faction-command-no-factions = No available guild. Connect to the server first.
rat-faction-command-available = Available Guilds:
rat-faction-command-none =   none - No guild
rat-faction-command-usage = 

Use: selectfaction <name> or selectfaction none
rat-faction-command-reset = Guild reset.
rat-faction-command-not-found = Guild '{ $factionName }' not found.
rat-faction-command-selected = Selected Guild: { $factionName }

# Guild Examine
rat-faction-examine = [color=gold]Guild: { $faction }[/color]

# Admin Guild Commands
rat-faction-admin-no-factions = No guilds in database.
rat-faction-admin-list-header = Guilds:
rat-faction-admin-total = 

Total guilds: { $count }
rat-faction-admin-created = Created guild '{ $name }' (whitelisted: { $whitelisted }).
rat-faction-admin-deleted = Deleted guild ID { $id }.
rat-faction-admin-delete-failed = Failed to delete guild ID { $id }. Guild may not exist.
rat-faction-admin-no-subfactions = No guilds found.
rat-faction-admin-list-columns = {"ID",-5} | {"Name",-30} | {"Whitelisted",-12} | Description
rat-faction-admin-yes = yes
rat-faction-admin-no = no
rat-faction-admin-set-manager = Set '{ $playerName }' as manager of guild ID { $factionId }.
rat-faction-admin-remove-manager = Removed '{ $playerName }' as manager of guild ID { $factionId }.
rat-faction-admin-invalid-boolean = Invalid boolean value: { $value }
rat-faction-admin-invalid-id = Invalid guild ID: { $id }. Must be a number.
rat-faction-admin-player-not-found = Player '{ $playerName }' not found.
rat-faction-admin-faction-not-found = Failed to set manager for guild ID { $factionId }. Guild may not exist or player is already a manager.
rat-faction-admin-remove-failed = Failed to remove manager from guild ID { $factionId }. Guild or manager may not exist.
rat-faction-admin-use-factionlist = Use 'guildlist' to see guild IDs

# UI
rat-faction-window-description = Choose a guild. Guilds are player-created groups where members can invite others and organize together.
rat-faction-window-title = Guild Selection
rat-faction-label = Select Guild
rat-faction-apply = Join
rat-faction-cancel = Cancel
rat-faction-button-text = Guild Selection