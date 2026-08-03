# Continuum room leave — invoked by the /continuum-leaveroom slash command.
# Prints an unbind marker into the session. The Stop-hook relay sees a LEAVE marker newer than the last
# bind marker and treats the session as no longer in a room, so the auto-relay stops.
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
Write-Output "Left the room. Automatic relay is now OFF for this session (your replies are no longer posted)."
Write-Output "(system marker for the relay — ignore this line)"
Write-Output "<<CONTINUUM-ROOM-LEAVE>>"
