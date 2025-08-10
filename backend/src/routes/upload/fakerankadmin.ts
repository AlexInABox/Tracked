export async function onRequestPost(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    try {
        const token = request.headers.get('Authorization');

        console.log('Upload fake rank admin POST request received with token:', token);
        console.log('Must match token:', env.APITOKEN);
        // Verify authorization token against environment variable
        if (!token || token !== env.APITOKEN) {
            return new Response('Unauthorized', { status: 401 });
        }

        // Parse and validate request body as dictionary with string keys and boolean values
        const requestData = (await request.json()) as Record<string, boolean>;

        // Validate that all values are booleans
        for (const [steamid, value] of Object.entries(requestData)) {
            if (typeof value !== 'boolean') {
                return new Response(`Invalid value for steamid "${steamid}": expected boolean`, { status: 400 });
            }
        }

        // Update fakerankadmin in database for each steamid
        for (const [steamid, value] of Object.entries(requestData)) {
            await env['zeitvertreib-data']
                .prepare(
                    `INSERT INTO playerdata (id, fakerankadmin) VALUES (?, ?) 
                         ON CONFLICT(id) DO UPDATE SET fakerankadmin = ?`,
                )
                .bind(steamid, value, value)
                .run();
        }

        return Response.json({
            success: true,
            message: `Fake rank admin status updated successfully for ${Object.keys(requestData).length} player(s)`,
            updated_data: requestData
        });
    } catch (error) {
        console.error('Upload fake rank admin POST error:', error);
        if (error instanceof SyntaxError) {
            return new Response('Invalid JSON', { status: 400 });
        }
        return new Response('Server error', { status: 500 });
    }
}
