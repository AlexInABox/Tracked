export async function onRequestPost(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    try {
        const token = request.headers.get("Authorization");
        
        console.log('Upload rounds POST request received with token:', token);
        console.log('Must match token:', env.APITOKEN);
        // Verify authorization token against environment variable
        if (!token || token !== env.APITOKEN) {
            return new Response("Unauthorized", { status: 401 });
        }
        
        // Parse and validate request body as dictionary with string keys and integer values
        const requestData = await request.json() as Record<string, number>;
        
        // Validate that all values are integers
        for (const [key, value] of Object.entries(requestData)) {
            if (typeof value !== 'number' || !Number.isInteger(value)) {
                return new Response(`Invalid value for key "${key}": expected integer`, { status: 400 });
            }
        }
        
        // Insert or update rounds played in database
        for (const [playerId, roundsPlayed] of Object.entries(requestData)) {
            await env["zeitvertreib-data"]
                .prepare(`INSERT INTO playerdata (id, roundsplayed) VALUES (?, ?) 
                         ON CONFLICT(id) DO UPDATE SET roundsplayed = COALESCE(roundsplayed, 0) + ?`)
                .bind(playerId, roundsPlayed, roundsPlayed)
                .run();
        }
        
        return Response.json({ success: true, message: "Rounds played data updated successfully" });
        
    } catch (error) {
        console.error('Upload rounds POST error:', error);
        if (error instanceof SyntaxError) {
            return new Response("Invalid JSON", { status: 400 });
        }
        return new Response("Server error", { status: 500 });
    }
}
