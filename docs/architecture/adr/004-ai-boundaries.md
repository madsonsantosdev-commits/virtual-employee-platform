# ADR-004 — Limites e responsabilidades da Inteligência Artificial

- **Status:** Accepted
- **Data:** 2026-09-10

## Contexto

A inteligência artificial é fundamental para oferecer uma experiência natural de funcionário virtual, porém permitir que um LLM seja autoridade direta sobre agenda, preços, pagamentos, refunds ou persistência criaria riscos de segurança, inconsistência e comportamento não determinístico.

Precisamos aproveitar IA para interpretação sem transferir a ela responsabilidade de domínio.

## Decisão

A IA será utilizada como **camada de interpretação e experiência conversacional**, nunca como autoridade final de negócio.

Regra principal:

> **IA interpreta → Backend valida → Domínio executa.**

Para operações financeiras:

> **IA interpreta → Usuário autoriza quando necessário → Backend valida → Domínio executa → Gateway processa → Webhook confirma.**

## AI Gateway

Toda integração com LLM deve passar pelo AI Gateway.

Responsabilidades:

- abstração de provedor/modelo;
- prompts;
- contexto;
- model routing;
- tokens;
- custos estimados;
- limites por tenant;
- fallback quando aplicável;
- telemetria.

## Proibições

A IA não pode:

- acessar diretamente o banco de dados;
- criar ou alterar registros sem passar pelos contratos de aplicação;
- inventar disponibilidade;
- definir ou negociar o preço oficial;
- receber/manipular dados brutos de cartão;
- confirmar pagamento;
- executar refund diretamente;
- calcular métricas financeiras como fonte de verdade;
- mover appointment de cliente sem consentimento quando o fluxo exigir autorização.

## Ferramentas determinísticas

A IA pode solicitar operações estruturadas como:

- `get_services()`;
- `get_prices()`;
- `get_available_slots()`;
- `create_booking()`;
- `reschedule_booking()`;
- `cancel_booking()`;
- `block_schedule()`;
- `get_payment_link()`;
- `get_business_information()`;
- `get_financial_summary(period)`.

Essas ferramentas chamam a camada de aplicação; não dão acesso direto à infraestrutura.

## Estratégia de custo

O produto deve evitar uso desnecessário de LLM.

Camadas conceituais:

- Layer 0: sem IA para confirmações determinísticas, webhooks e estados;
- Layer 1: modelo econômico para classificação simples;
- Layer 2: modelo conversacional para interpretação natural;
- Layer 3: modelo avançado somente quando necessário.

## Consequências positivas

- maior previsibilidade;
- segurança financeira;
- domínio testável sem LLM;
- possibilidade de trocar modelos/provedores;
- melhor controle de custos por tenant;
- redução do impacto de alucinações.

## Consequências negativas

- exige desenho explícito de tools/contratos;
- Conversation Engine precisa gerenciar estado;
- algumas respostas exigem chamadas adicionais ao backend.
