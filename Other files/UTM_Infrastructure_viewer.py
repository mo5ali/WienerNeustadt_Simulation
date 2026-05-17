import json
from math import sqrt
import plotly.graph_objects as go

# Class Definitions
class Segment:
    def __init__(self, start_node, end_node, length, parent_track):
        self.start_node = start_node
        self.end_node = end_node
        self.length = length
        self.parent_track = parent_track


class Node:
    def __init__(self, _type, _ID, point_ID, easting, northing):
        self._type = _type
        self._ID = _ID
        self.point_ID = point_ID
        self.easting = easting
        self.northing = northing


class Track:
    def __init__(self, ID, TrackID, length, start_node=None, end_node=None, MapID=None, area=None):
        self.ID = ID
        self.TrackID = TrackID
        self.length = length
        self.start_node = start_node
        self.end_node = end_node
        self.MapID = MapID
        self.area = area
        self.nodes = []
        self.segments = []


# Function to calculate Euclidean distance in UTM coordinates
def calculate_distance(x1, y1, x2, y2):
    """
    Calculate the distance between two points using vector magnitude.
    Mimics the logic from the C# Vector class.
    """
    # Calculate the vector components
    x = round(x2 - x1, 7)
    y = round(y2 - y1, 7)

    # Calculate the magnitude of the vector
    magnitude = round((x**2 + y**2)**0.5, 7)

    return magnitude


# Load JSON data
with open("C:\\Users\\moham\\source\\repos\\WienerNeustadt_Simulation\\InputFiles\\Infrastructure_WienerNeustadt_V20.json") as f:
    json_data = json.load(f)

# Create dictionaries for nodes and tracks
nodes = {}
tracks = []

# Populate nodes
for node_data in json_data["Switches"]:
    node = Node(
        _type="Switch",
        _ID=node_data["Id"],
        point_ID=node_data["PointId"],
        easting=node_data["X"],
        northing=node_data["Y"],
    )
    nodes[node.point_ID] = node

for node_data in json_data["Bends"]:
    node = Node(
        _type="Bend",
        _ID=node_data["Id"],
        point_ID=node_data["PointId"],
        easting=node_data["X"],
        northing=node_data["Y"],
    )
    nodes[node.point_ID] = node

for node_data in json_data["ShuntingSignals"]:
    node = Node(
        _type="ShuntingSignal",
        _ID=node_data["Id"],
        point_ID=node_data["PointId"],
        easting=node_data["X"],
        northing=node_data["Y"],
    )
    nodes[node.point_ID] = node

# Create Track and Segment objects
for track_data in json_data["TrackSegments"]:
    railway_station_area = track_data.get("RailwayStationArea", "Unknown")
    track = Track(
        ID=track_data["Id"],
        TrackID=track_data["TrackId"],
        length=0,  # Initialize with 0, will recalculate based on segments
        MapID=track_data["MapId"],
        area=railway_station_area,
    )

    start_node = nodes.get(track_data["ConnectionPoint1Id"])
    end_node = nodes.get(track_data["ConnectionPoint2Id"])
    if start_node:
        track.start_node = start_node
    if end_node:
        track.end_node = end_node

    interim_nodes = [nodes.get(point_id) for point_id in track_data["InterimPointIds"]]

    if start_node and end_node:
        if not interim_nodes:
            midpoint_easting = (start_node.easting + end_node.easting) / 2
            midpoint_northing = (start_node.northing + end_node.northing) / 2
            midpoint_node = Node(
                _type="Midpoint", _ID=-1, point_ID=-1, easting=midpoint_easting, northing=midpoint_northing
            )
            interim_nodes = [midpoint_node]
            segment_length = calculate_distance(
                start_node.easting, start_node.northing, midpoint_node.easting, midpoint_node.northing
            )
            track.segments.append(Segment(start_node, midpoint_node, segment_length, track))

        for i in range(len(interim_nodes)):
            if i == 0:
                segment_start = start_node
                segment_end = interim_nodes[i]
            else:
                segment_start = interim_nodes[i - 1]
                segment_end = interim_nodes[i]
            segment_length = calculate_distance(
                segment_start.easting, segment_start.northing, segment_end.easting, segment_end.northing
            )
            track.segments.append(Segment(segment_start, segment_end, segment_length, track))

        if interim_nodes:
            segment_start = interim_nodes[-1]
            segment_end = end_node
            segment_length = calculate_distance(
                segment_start.easting, segment_start.northing, segment_end.easting, segment_end.northing
            )
            track.segments.append(Segment(segment_start, segment_end, segment_length, track))

    # Recalculate the total length of the track
    track.length = sum(segment.length for segment in track.segments)

    tracks.append(track)

# Initialize Plotly figure
fig = go.Figure()

# Visualize the tracks
for track in tracks:
    map_id_str = str(track.MapID)
    if track.area == "Group600":
        track_color = "green"
    elif track.area == "Group700":
        track_color = "brown"
    elif track.area == "Classification_MA":
        track_color = "orange"
    else:
        track_color = "blue"

    for segment in track.segments:
        fig.add_trace(go.Scatter(
            x=[segment.start_node.easting, segment.end_node.easting],
            y=[segment.start_node.northing, segment.end_node.northing],
            mode="lines",
            line=dict(width=2, color=track_color),
            name=f"Track {track.TrackID}",
            hoverinfo="text",
            hovertext=f"Track ID: {track.ID}<br>MapID: {track.MapID}<br>Length: {track.length:.2f} m<br>Start Node: {track.start_node.point_ID}<br>End Node: {track.end_node.point_ID}",
        ))

# Plot nodes with updated hover text for bends
for node in nodes.values():
    if node._type == "Switch":
        color = "red"
        hover_text = f"Switch<br>Point ID: {node.point_ID}<br>ID: {node._ID}"
    elif node._type == "Bend":
        color = "teal"
        # Find associated tracks
        associated_tracks = [
            track for track in tracks if any(
                segment.start_node == node or segment.end_node == node for segment in track.segments
            )
        ]
        hover_text = f"Bend<br>Point ID: {node.point_ID}<br>ID: {node._ID}"
        if associated_tracks:
            for track in associated_tracks:
                hover_text += f"<br>Track MAP ID: {track.MapID}<br>Track Length: {track.length:.2f} m"
    elif node._type == "ShuntingSignal":
        color = "#b300b3"
        hover_text = f"Shunting Signal<br>Point ID: {node.point_ID}<br>ID: {node._ID}"
    else:
        color = "gray"
        hover_text = f"Node<br>Point ID: {node.point_ID}<br>ID: {node._ID}"

    fig.add_trace(go.Scatter(
        x=[node.easting],
        y=[node.northing],
        mode="markers",
        marker=dict(size=8, color=color, symbol="circle"),
        name=f"{node._type} {node.point_ID}",
        hoverinfo="text",
        hovertext=hover_text,
    ))

# Update layout for Plotly figure
fig.update_layout(
    title="Railway Infrastructure Visualization (UTM)",
    xaxis=dict(title="Easting (m)"),
    yaxis=dict(title="Northing (m)"),
    legend=dict(title="Legend"),
)

# Show the figure
fig.show()
