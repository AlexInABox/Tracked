import * as uploadTimes from './routes/upload/times';
import * as uploadKills from './routes/upload/kills';
import * as uploadRounds from './routes/upload/rounds';
import * as uploadMedkits from './routes/upload/medkits';
import * as uploadColas from './routes/upload/colas';
import * as uploadAdrenaline from './routes/upload/adrenaline';
import * as uploadPocketEscapes from './routes/upload/pocketescapes';
import * as uploadPlayerPoints from './routes/upload/playerpoints';
import * as uploadSnake from './routes/upload/snake';
import * as uploadFakeRankAllowed from './routes/upload/fakerankallowed';
import * as uploadFakeRankAdmin from './routes/upload/fakerankadmin';
import * as experience from './routes/experience';


const routes: Record<string, any> = {
	'/upload/times': uploadTimes,
	'/upload/kills': uploadKills,
	'/upload/rounds': uploadRounds,
	'/upload/medkits': uploadMedkits,
	'/upload/colas': uploadColas,
	'/upload/adrenaline': uploadAdrenaline,
	'/upload/pocketescapes': uploadPocketEscapes,
	'/upload/playerpoints': uploadPlayerPoints,
	'/upload/snake': uploadSnake,
	'/upload/fakerankallowed': uploadFakeRankAllowed,
	'/upload/fakerankadmin': uploadFakeRankAdmin,
	'/experience': experience,
};

export default {
	async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
		const url = new URL(request.url);
		const route = routes[url.pathname];

		if (!route) {
			return new Response('Not found', { status: 404 });
		}

		const handler = route[`onRequest${request.method.charAt(0).toUpperCase() + request.method.slice(1).toLowerCase()}`] || route.onRequest;

		if (!handler) {
			return new Response('Method not allowed', { status: 405 });
		}

		return handler(request, env, ctx);
	},
};
