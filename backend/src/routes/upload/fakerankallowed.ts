export async function onRequestPost(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    try {
        const token = request.headers.get('Authorization');

        console.log('Upload fake rank allowed POST request received with token:', token);
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

        // Calculate timestamp for 14 days from now
        const fourteenDaysFromNow = Math.floor(Date.now() / 1000) + (14 * 24 * 60 * 60);

        // Update fakerank_until in database for each steamid (only grant permissions, never revoke)
        for (const [steamid, value] of Object.entries(requestData)) {
            if (value) {
                // If granting fakerank permissions, check current timestamp and set to 14 days from now if needed
                const currentPlayer = await env['zeitvertreib-data']
                    .prepare(`SELECT fakerank_until FROM playerdata WHERE id = ?`)
                    .bind(steamid)
                    .first();

                const currentTimestamp = (currentPlayer?.fakerank_until as number) || 0;
                const newTimestamp = Math.max(currentTimestamp, fourteenDaysFromNow);

                await env['zeitvertreib-data']
                    .prepare(
                        `INSERT INTO playerdata (id, fakerank_until) VALUES (?, ?) 
                         ON CONFLICT(id) DO UPDATE SET fakerank_until = ?`,
                    )
                    .bind(steamid, newTimestamp, newTimestamp)
                    .run();
            }
            // Note: No revocation logic - fakerank permissions are never removed via this endpoint
        }

        return Response.json({
            success: true,
            message: `Fake rank permissions updated successfully for ${Object.keys(requestData).length} player(s)`,
            updated_data: requestData
        });
    } catch (error) {
        console.error('Upload fake rank allowed POST error:', error);
        if (error instanceof SyntaxError) {
            return new Response('Invalid JSON', { status: 400 });
        }
        return new Response('Server error', { status: 500 });
    }
}
